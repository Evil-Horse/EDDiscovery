﻿/*
 * Copyright 2019-2023 Robbyxp1 @ github.com
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 */

using EliteDangerousCore;
using EliteDangerousCore.DB;
using GLOFC;
using GLOFC.GL4;
using GLOFC.GL4.Buffers;
using GLOFC.GL4.Shaders;
using GLOFC.GL4.Shaders.Fragment;
using GLOFC.GL4.Shaders.Geo;
using GLOFC.GL4.Shaders.Stars;
using GLOFC.GL4.Shaders.Vertex;
using GLOFC.GL4.ShapeFactory;
using GLOFC.GL4.Textures;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace EDDiscovery.UserControls.Map3D
{
    public class GalaxyStars
    {
        private Vector3 InvalidPos = new Vector3(-1000000, -1000000, -1000000);
        public Vector3 CurrentPos { get; set; } = new Vector3(-1000000, -1000000, -1000000);
        public Font Font { get; set; } = new Font("Arial", 8.5f);
        public Color ForeText { get; set; } = Color.FromArgb(255,220,220,220);
        public Color BackText { get; set; } = Color.Transparent;
        public Vector3 LabelSize { get; set; } = new Vector3(5, 0, 5f / 4f);
        public Vector3 LabelOffset { get; set; } = new Vector3(0, -1f, 0);
        public Size TextBitMapSize { get; set; } = new Size(160, 16);
        // 0 = off, bit 0= stars, bit1 = labels
        public int EnableMode { get { return enablemode; } set { enablemode = value; sunshader.Enable = (enablemode & 1) != 0; textshader.Enable = enablemode == 3; } }
        public int MaxObjectsAllowed { get; set; } = 100000;
        public bool DBActive { get { return subthreadsrunning > 0; ; } }
        public bool ShowDistance { get; set; } = false;     // at the moment, can't use it, due to clashing with travel path stars
        public int SectorSize { get; set; } = 100;


        public HashSet<GalMapObjects.ObjectPosXYZ> NoSunList = new HashSet<GalMapObjects.ObjectPosXYZ>();
        public Vector3 NoSunTextOffset { get; set; } = new Vector3(0, -1.2f, 0);

        public void Create(GLItemsList items, GLRenderProgramSortedList rObjects, Tuple<GLTexture2DArray, long[]> starimagearrayp, float sunsize, GLStorageBlock findbufferresults)
        {
            // globe shape
            var shape = GLSphereObjectFactory.CreateTexturedSphereFromTriangles(2, sunsize);

            // globe vertex
            starshapebuf = items.NewBuffer(false);
            starshapebuf.AllocateFill(shape.Item1);

            // globe text coords

            startexcoordbuf = items.NewBuffer(false);
            startexcoordbuf.AllocateFill(shape.Item2);

            // a texture 2d array with various star images
            starimagearray = starimagearrayp;

            // the sun shader

            sunvertexshader = new GLPLVertexShaderModelWorldTextureAutoScale(autoscale: 50, autoscalemin: 1f, autoscalemax: 50f, useeyedistance: false);
            var sunfragmenttexture = new GLPLFragmentShaderTexture2DWSelectorSunspot();
            sunshader = new GLShaderPipeline(sunvertexshader, sunfragmenttexture);
            items.Add(sunshader);

            var starrc = GLRenderState.Tri();     // render is triangles
            starrc.DepthTest = true;
            //starrc.DepthClamp = true;
            GLRenderDataTexture starrdt = new GLRenderDataTexture(starimagearray.Item1);  // RDI is used to attach the texture

            var textrc = GLRenderState.Tri();
            textrc.DepthTest = true;
            textrc.ClipDistanceEnable = 1;  // we are going to cull primitives which are deleted

            int texunitspergroup = 16;
            textshader = items.NewShaderPipeline(null, new GLPLVertexShaderMatrixTriStripTexture(), new GLPLFragmentShaderTexture2DIndexMulti(0, 0, true, texunitspergroup));

            slset = new GLSetOfObjectsWithLabels("SLSet", rObjects, texunitspergroup, 100, 10,
                                                            sunshader, starshapebuf, startexcoordbuf, shape.Item1.Length, starrc, OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, starrdt,
                                                            textshader, TextBitMapSize, textrc, SizedInternalFormat.Rgba8);

            items.Add(slset);

            // for debug
           // GLStorageBlock debugbuf = items.NewStorageBlock(5); debugbuf.AllocateBytes(3200000); var geofind = new GLPLGeoShaderFindTriangles(findbufferresults, 32768, debugbuffer: debugbuf);
            var geofind = new GLPLGeoShaderFindTriangles(findbufferresults, 32768);

            findshader = items.NewShaderPipeline(null, sunvertexshader, null, null, geofind, null, null, null);
        }

        public void Start()
        {
            requestorthread = new Thread(Requestor);
            requestorthread.Start();
        }

        public void Stop()
        {
            //System.Diagnostics.Debug.WriteLine("Request stop on gal stars");
            stop.Cancel();
            requestorthread.Join();
            while(subthreadsrunning > 0)
            {
                System.Diagnostics.Debug.WriteLine("Sub thread running");
                Thread.Sleep(100);
            }
            System.Diagnostics.Debug.WriteLine("Stopped on gal stars");

            while (cleanbitmaps.TryDequeue(out Sector sectoclean))
            {
                System.Diagnostics.Debug.WriteLine($"Final Clean bitmap for {sectoclean.pos}");
                GLOFC.Utils.BitMapHelpers.Dispose(sectoclean.bitmaps);
                sectoclean.bitmaps = null;
            }
        }

        // using CurrentPosition as the conditional
        public void Request9x3BoxConditional(Vector3 newpos)        
        {
            // if out of pos, not too many requestes, and rebuild is not running
            if ((CurrentPos - newpos).Length >= SectorSize && requestedsectors.Count < MaxRequests )
            {
                Request9x3Box(newpos);
                CurrentPos = newpos;
            }
        }

        // request a 9x3 box
        public void Request9x3Box(Vector3 pos)
        {
            //System.Diagnostics.Debug.WriteLine($"Request 9 box ${pos}");

            for (int i = 0; i <= 2; i++)
            {
                int y = i == 0 ? 0 : i == 1 ? SectorSize : -SectorSize;
                RequestBox(new Vector3(pos.X , pos.Y + y, pos.Z));
                RequestBox(new Vector3(pos.X + SectorSize, pos.Y + y, pos.Z));
                RequestBox(new Vector3(pos.X - SectorSize, pos.Y + y, pos.Z));
                RequestBox(new Vector3(pos.X, pos.Y+y, pos.Z + SectorSize));
                RequestBox(new Vector3(pos.X, pos.Y + y, pos.Z - SectorSize));
                RequestBox(new Vector3(pos.X + SectorSize, pos.Y + y, pos.Z + SectorSize));
                RequestBox(new Vector3(pos.X + SectorSize, pos.Y + y, pos.Z - SectorSize));
                RequestBox(new Vector3(pos.X - SectorSize, pos.Y + y, pos.Z + SectorSize));
                RequestBox(new Vector3(pos.X - SectorSize, pos.Y + y, pos.Z - SectorSize));
            }
            //System.Diagnostics.Debug.WriteLine($"End 9 box");
        }

        // request a box around pos (nominalised to sectorsize) and if not already requested, send the request to the requestor using a blocking queue
        public void RequestBox(Vector3 pos)
        {
            int mm = 100000 + SectorSize / 2;
            pos.X = (int)(pos.X + mm) / SectorSize * SectorSize - mm;
            pos.Y = (int)(pos.Y + mm) / SectorSize * SectorSize - mm;
            pos.Z = (int)(pos.Z + mm) / SectorSize * SectorSize - mm;

            if (!slset.TagsToBlocks.ContainsKey(pos))
            {
                var sec = new Sector(pos, SectorSize);
                slset.ReserveTag(sec.pos);      // important, stops repeated adds in the situation where it takes a while to add to set
                //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} {pos} request box");
                requestedsectors.Add(sec);
            }
            else
            {
                //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} {pos} request rejected");
            }
        }

        private float previousboxaroundsize;
        public void RequestBoxAround(Vector3 pos, int size)   // pos is the centre
        {
            CurrentPos = pos;
            previousboxaroundsize = size;
            var sec = new Sector(new Vector3(pos.X - size / 2, pos.Y - size / 2, pos.Z - size / 2), size);
            slset.ReserveTag(sec.pos);      // unconditional reserve of this tag
            //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} Galaxy {sec.pos} request box around");
            requestedsectors.Add(sec);
        }
        public void ClearBoxAround()        // clear the box around
        {
            // removing the sector from the list, we still might have its operation outstanding, so be careful with the draw in Update

            var sec = new Vector3(CurrentPos.X - previousboxaroundsize / 2, CurrentPos.Y - previousboxaroundsize / 2, CurrentPos.Z - previousboxaroundsize / 2);
            slset.Remove(sec);   
            
            CurrentPos = InvalidPos;
        }

        public void Clear(Vector3 pos)
        {
            slset.Remove(pos);
        }

        // clear all objects
        public void Clear()
        {
            slset.Clear();      // note this clears the current painted objects, if there are some flying, it won't clear those
            CurrentPos = InvalidPos;
        }

        // do this in a thread, as adding threads is computationally expensive so we don't want to do it in the foreground
        private void Requestor()
        {
            while (true)
            {
                try
                {
                    //  System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} Requestor take");
                    var sector = requestedsectors.Take(stop.Token);       // blocks until take or told to stop

                    do
                    {
                        // reduce memory use first by bitmap cleaning 

                        lock (cleanbitmaps)     // foreground also does this if required
                        {
                            while (cleanbitmaps.TryDequeue(out Sector sectoclean))
                            {
                                //System.Diagnostics.Debug.WriteLine($"Clean bitmap for {sectoclean.pos}");
                                GLOFC.Utils.BitMapHelpers.Dispose(sectoclean.bitmaps);
                                sectoclean.bitmaps = null;
                            }
                        }

                        //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} Galaxy {sector.pos} requestor accepts");

                        Interlocked.Add(ref subthreadsrunning, 1);      // committed to run, and count subthreads

                        Thread p = new Thread(FillSectorThread);
                        p.Start(sector);

                        while (subthreadsrunning >= MaxSubthreads)      // pause the take if we have too much work going on
                            Thread.Sleep(100);

                    } while (requestedsectors.TryTake(out sector));     // until empty..

                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            //System.Diagnostics.Debug.WriteLine("Exit requestor");
        }

        // in a thread, look up the sector 
        private void FillSectorThread(Object seco)
        {
            Sector d = (Sector)seco;

            //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} Galaxy Thread start for {d.pos}");

            // note d.text/d.positions may be much longer than d.systems

            if (d.searchsize > 0)           // if not a clear..
            {
                if (ShowDistance)
                {
                    Vector4 pos = new Vector4(d.pos.X + d.searchsize / 2, d.pos.Y + d.searchsize / 2, d.pos.Z + d.searchsize / 2, 0);       // from centre of box

                    d.systems = SystemsDB.GetSystemList(d.pos.X, d.pos.Y, d.pos.Z, d.searchsize, ref d.text, ref d.positions,
                        (x, y, z, startype) => {
                            if (NoSunList.Contains(new GalMapObjects.ObjectPosXYZ(x, y, z)))
                                return new Vector4((float)x / SystemClass.XYZScalar, (float)y / SystemClass.XYZScalar, (float)z / SystemClass.XYZScalar, -1);
                            else
                                return new Vector4((float)x / SystemClass.XYZScalar, (float)y / SystemClass.XYZScalar, (float)z / SystemClass.XYZScalar, starimagearray.Item2[(int)startype]); 
                        },
                        (v, s) => { 
                            var dist = (pos - v).Length; return s + $" @ {dist:0.#}ly"; 
                        });
                }
                else
                {
                    d.systems = SystemsDB.GetSystemList(d.pos.X, d.pos.Y, d.pos.Z, d.searchsize, ref d.text, ref d.positions,
                                            (x, y, z, startype) => {
                                                if (NoSunList.Contains(new GalMapObjects.ObjectPosXYZ(x, y, z)))
                                                    return new Vector4((float)x / SystemClass.XYZScalar, (float)y / SystemClass.XYZScalar, (float)z / SystemClass.XYZScalar, -1);
                                                else
                                                    return new Vector4((float)x / SystemClass.XYZScalar, (float)y / SystemClass.XYZScalar, (float)z / SystemClass.XYZScalar, starimagearray.Item2[(int)startype]);
                                            },
                                            null);
                }

                if (d.systems > 0)      // may get nothing, so don't do this if so
                {
                    // note only draw d.systems
                    using (StringFormat fmt = new StringFormat())
                    {
                        fmt.Alignment = StringAlignment.Center;

                        d.bitmaps = GLOFC.Utils.BitMapHelpers.DrawTextIntoFixedSizeBitmaps(slset.LabelSize, d.text, Font, System.Drawing.Text.TextRenderingHint.ClearTypeGridFit,
                                                ForeText, BackText, 0.5f, textformat: fmt, length: d.systems);
                    }

                    d.textpos = CreateMatrices2(d.positions, LabelOffset,  NoSunTextOffset,
                                                                                LabelSize, //size
                                                                                new Vector3(0, 0, 0), // rot (unused due to below)
                                                                                true, false, // rotate, no elevation
                                                                                length: d.systems    // limit length
                                                                                );
                }
            }

            generatedsectors.Enqueue(d);       // d has been filled
            //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} Galaxy Thread End {d.pos} {d.systems}");

            Interlocked.Add(ref subthreadsrunning, -1);
        }

        public Matrix4[] CreateMatrices2(Vector4[] worldpos, Vector3 offset, Vector3 disallowedoffset,
                                            Vector3 size, Vector3 rotationradians,
                                            bool rotatetoviewer, bool rotateelevation,
                                            float alphafadescalar = 0,
                                            float alphafadepos = 0,
                                            int imagepos = 0,
                                            bool visible = true,
                                            int pos = 0, int length = -1        // allowing you to pick out a part of the worldpos array
                                            )
        {
            if (length == -1)
                length = worldpos.Length - pos;

            Matrix4[] mats = new Matrix4[length];
            for (int i = 0; i < length; i++)
            {
                Vector3 doff = worldpos[i + pos].W < 0 ? (disallowedoffset + offset) : offset;
                mats[i] = GLStaticsMatrix4.CreateMatrix(worldpos[i + pos].Xyz + doff, size, rotationradians, rotatetoviewer, rotateelevation, alphafadescalar, alphafadepos, imagepos, visible);
            }
            return mats;
        }


        ulong timelastchecked = 0;
        ulong timelastbitmapcheck = 0;

        // foreground, called on each frame, allows update of shader and queuing of new objects
        public void Update(ulong time, float eyedistance)
        {
            if (time - timelastchecked > 50)
            {
                if (generatedsectors.Count > 0)
                {
                    int max = 2;
                    while (max-- > 0 && generatedsectors.TryDequeue(out Sector d) )      // limit fill rate.. (max first)
                    {
                        //System.Diagnostics.Debug.WriteLine($"{Environment.TickCount % 100000} Galaxy Add {d.pos} number {d.systems} total {slset.Objects}");
                        
                        // so due to the threads being async, as we can get in a condition where we cancel a block using ClearBoxAround
                        // we could be get a completed block which is not required. So use ContainsKey to make sure the system still wants
                        // that system.
                        if (d.systems > 0 && slset.TagsToBlocks.ContainsKey(d.pos))
                        {
                            slset.Add(d.pos, d.text, d.positions, d.textpos, d.bitmaps, 0, d.systems);
                            cleanbitmaps.Enqueue(d);            // ask for cleaning of these bitmaps
                        }

                        d.positions = null;     // don't need these
                        d.textpos = null;           // and these are not needed

                        if (slset.Objects > MaxObjectsAllowed)       // if set, and active
                        {
                            slset.RemoveUntil(MaxObjectsAllowed - MaxObjectsMargin);
                        }

                        //System.Diagnostics.Debug.WriteLine($"..add complete {d.pos} {slset.Objects}" );
                    }
                }

                timelastchecked = time;
            }

            if ( time - timelastbitmapcheck > 60000)    // only do this infrequently
            {
                lock (cleanbitmaps)     // foreground also does this if required
                {
                    while (cleanbitmaps.TryDequeue(out Sector sectoclean))
                    {
                        //System.Diagnostics.Debug.WriteLine($"Foreground Clean bitmap for {sectoclean.pos}");
                        GLOFC.Utils.BitMapHelpers.Dispose(sectoclean.bitmaps);
                        sectoclean.bitmaps = null;
                    }
                }

                timelastbitmapcheck = time;
            }

            const int rotperiodms = 10000;
            time = time % rotperiodms;
            float fract = (float)time / rotperiodms;
            float angle = (float)(2 * Math.PI * fract);
            float scale = Math.Max(1, Math.Min(4, eyedistance / 5000));

            sunvertexshader.ModelTranslation = Matrix4.CreateRotationY(-angle);
            sunvertexshader.ModelTranslation *= Matrix4.CreateScale(scale);
        }

        // returns system class but with name only, and z - if not found z = Max value, null
        public SystemClass Find(Point loc, GLRenderState rs, Size viewportsize, out float z)
        {
            z = float.MaxValue;

            if (sunshader.Enable)
            {
                var findlist = slset.Find(findshader, rs, loc, viewportsize, 4);
                if ( findlist != null )
                {
                    foreach (var fl in findlist)
                    {
                        var udid = slset.FindUserData(fl);
                        var stringuserdatad = slset.UserData[udid.Item1[0].tag] as string[];
                        string named = stringuserdatad[udid.Item2];
                        System.Diagnostics.Debug.WriteLine($"Found {fl} = {named}");
                    }

                    System.Diagnostics.Debug.WriteLine($"Galaxy find {findlist.Length}");

                    var udi = slset.FindUserData(findlist[0]);
                    if (udi != null)
                    {
                        var stringuserdata = slset.UserData[udi.Item1[0].tag] as string[];
                        string name = stringuserdata[udi.Item2];
                        int atsign = name.IndexOf(" @");        // remove any information denoted by space @
                        if (atsign >= 0)
                            name = name.Substring(0, atsign);
                        z = findlist[0].Item4;
                        return new SystemClass() { Name = name };       // without position note
                    }
                }
            }

            return null;
        }



        private GLSetOfObjectsWithLabels slset; // main class holding drawing

        private GLPLVertexShaderModelWorldTextureAutoScale sunvertexshader;
        private GLShaderPipeline sunshader;     // sun drawer
        private GLShaderPipeline textshader;     // text shader
        private GLBuffer starshapebuf;
        private GLBuffer startexcoordbuf;
        private Tuple<GLTexture2DArray, long[]> starimagearray;

        private GLShaderPipeline findshader;    // find shader for lookups

        private class Sector
        {
            public Vector3 pos;             // position
            public int searchsize;          // and size.. nominally SectorSize, but for local map its the search size
            public Sector(Vector3 pos, int size) { this.pos = pos; searchsize = size; }

            // generated by thread, passed to update, bitmaps pushed to cleanbitmaps and deleted by requestor
            public int systems;
            public Vector4[] positions;
            public string[] text;
            public Matrix4[] textpos;
            public Bitmap[] bitmaps;
        }

        // requested sectors from foreground to requestor
        private BlockingCollection<Sector> requestedsectors = new BlockingCollection<Sector>();

        // added to by subthread when sector is ready, picked up by foreground update. ones ready for final foreground processing
        private ConcurrentQueue<Sector> generatedsectors = new ConcurrentQueue<Sector>();

        // added to by update when cleaned up bitmaps, requestor will clear these for it
        private ConcurrentQueue<Sector> cleanbitmaps = new ConcurrentQueue<Sector>();

        private Thread requestorthread;
        private CancellationTokenSource stop =  new CancellationTokenSource();
        private int subthreadsrunning = 0;

        private int enablemode = 3;
        private const int MaxObjectsMargin = 1000;
        private const int MaxRequests = 27 * 2;
        private const int MaxSubthreads = 16;
    }

}
