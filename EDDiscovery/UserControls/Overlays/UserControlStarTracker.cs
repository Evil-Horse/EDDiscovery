/*
 * Copyright © 2016 - 2023 EDDiscovery development team
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
using EliteDangerousCore.JournalEvents;
using OpenTK;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms.VisualStyles;
using System.Windows.Media.Media3D;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;

namespace EDDiscovery.UserControls
{
    public partial class UserControlStarTracker : UserControlCommonBase
    {
        private string dbLatSave = "LatTarget";
        private string dbLongSave = "LongTarget";
        private string dbFont = "Font";

        EliteDangerousCore.UIEvents.UIPosition position = new EliteDangerousCore.UIEvents.UIPosition();

        EliteDangerousCore.UIEvents.UIMode elitemode;
        private bool intransparent = false;
        private EliteDangerousCore.UIEvents.UIGUIFocus.Focus guistate;

        private Font displayfont;

        private ISystem current_sys;   // current system we are in, may be null, picked up from last_he
        private string current_body;    // current body, picked up from last_he, and checked via UI Position for changes
        private StarScan.SystemNode current_data;
        private int current_body_id;

        private string sentbookmarktext;    // Another panel has sent a bookmark, and its position, add it to the combobox and allow selection
        private EliteDangerousCore.UIEvents.UIPosition.Position sentposition;

        double lat;
        double lon;
        double alt;

        double x;
        double y;
        double z;

        StringBuilder csv = new StringBuilder();
        StringBuilder showStr = new StringBuilder();
        DateTime tempDate;
        double tempValue;
        string culminationString;

        Dictionary<int, StarScan.ScanNode> allBodies = new Dictionary<int, StarScan.ScanNode>();
        Dictionary<int, Vector3d> allBodiesCoordinates = new Dictionary<int, Vector3d>();

        #region Init

        public UserControlStarTracker()
        {
            InitializeComponent();
        }

        public override void Init()
        {
            DBBaseName = "StarTracker";

            double lat = GetSetting(dbLatSave, double.NaN);     // pick up target, it will be Nan if not set
            double lon = GetSetting(dbLongSave, double.NaN);

            PopulateCtrlList();

            DiscoveryForm.OnNewEntry += OnNewEntry;
            DiscoveryForm.OnNewUIEvent += OnNewUIEvent;
            DiscoveryForm.OnHistoryChange += Discoveryform_OnHistoryChange;

            // new! april23 pick up last major mode ad uistate.  Need to do this now, before load/initial display, as set transparent uses it
            elitemode = new EliteDangerousCore.UIEvents.UIMode(DiscoveryForm.UIOverallStatus.Mode, DiscoveryForm.UIOverallStatus.MajorMode);
            guistate = DiscoveryForm.UIOverallStatus.Focus;
        }

        public override void LoadLayout()
        {
            base.LoadLayout();
        }

        public override void InitialDisplay()       
        {
            Discoveryform_OnHistoryChange();
        }

        public override void Closing()
        {
            DiscoveryForm.OnNewEntry -= OnNewEntry;
            DiscoveryForm.OnNewUIEvent -= OnNewUIEvent;
            DiscoveryForm.OnHistoryChange -= Discoveryform_OnHistoryChange;

            File.WriteAllText("D:\\ED.csv", csv.ToString());
        }

        private void Discoveryform_OnHistoryChange() 
        {
            var lasthe = DiscoveryForm.History.GetLast;
            current_sys = lasthe?.System;       // pick up last system and body 
            current_body = lasthe?.Status.BodyName;
            UpdateData();
            UpdateStarTracker();
            SetStarTrackerVisibility();
        }

        public override bool SupportTransparency { get { return true; } }
        public override bool DefaultTransparent { get { return true; } }
        public override void SetTransparency(bool on, Color curbackcol)
        {
            BackColor = curbackcol;

            flowLayoutPanelTop.BackColor = curbackcol;

            flowLayoutPanelTop.Visible = !on;

            intransparent = on;
            SetStarTrackerVisibility();
        }

        // UI event in, accumulate state information
        private void OnNewUIEvent(UIEvent uievent)       
        {
            EliteDangerousCore.UIEvents.UIMode mode = uievent as EliteDangerousCore.UIEvents.UIMode;
            if ( mode != null )
            {
                elitemode = mode;
                System.Diagnostics.Debug.WriteLine($"StarTracker Elitemode {elitemode.MajorMode} {elitemode.Mode}");
                SetStarTrackerVisibility();
            }

            EliteDangerousCore.UIEvents.UIPosition pos = uievent as EliteDangerousCore.UIEvents.UIPosition;

            if (pos != null)
            {
                position = pos;

                System.Diagnostics.Debug.WriteLine($"StarTracker lat {pos.Location.Latitude}, {pos.Location.Longitude} A {pos.Location.Altitude} H {pos.Heading} R {pos.PlanetRadius} BN {pos.BodyName}");

                lat = pos.Location.Latitude;
                lon = pos.Location.Longitude;
                alt = pos.Location.Altitude;

                if (pos.BodyName != current_body)
                {
                    current_body = pos.BodyName;
                }

                if (current_data == null)
                {
                    UpdateData();
                }

                UpdateStarTracker();
                SetStarTrackerVisibility();
            }

            EliteDangerousCore.UIEvents.UIBodyName bn = uievent as EliteDangerousCore.UIEvents.UIBodyName;

            if ( bn != null )
            {
                current_body = bn.BodyName;
                System.Diagnostics.Debug.WriteLine($"StarTracker changed body name {current_body}");
                UpdateData();

                double rotationPeriod = 0.0;
                foreach (var body in current_data.Bodies)
                {
                    if (body.BodyID == current_body_id)
                    {
                        rotationPeriod = body.ScanData.nRotationPeriod.GetValueOrDefault(-1); break;
                    }
                }
                string str = $"Body name {bn.BodyName}, rotation period {rotationPeriod}\n";
                csv.Append(str);
            }

            var gui = uievent as EliteDangerousCore.UIEvents.UIGUIFocus;

            if ( gui != null )
            {
                guistate = gui.GUIFocus;
                System.Diagnostics.Debug.WriteLine($"StarTracker changed GUI state {guistate}");
                SetStarTrackerVisibility();
            }

            EliteDangerousCore.UIEvents.UITemperature temperatureEvent = uievent as EliteDangerousCore.UIEvents.UITemperature;
            if (temperatureEvent != null)
            {
                double datediff = (temperatureEvent.EventTimeUTC - tempDate).TotalSeconds;
                double tempdiff = temperatureEvent.Temperature - tempValue;

                // what temperature will be in 10 minutes?
                if (datediff > 0)
                {
                    double tenMinutesTempDiff = temperatureEvent.Temperature + tempdiff * (600 / datediff);
                    System.Diagnostics.Debug.WriteLine($"New data: {datediff} seconds, {tempdiff} degrees");
                    System.Diagnostics.Debug.WriteLine($"Within 10 minutes: {tenMinutesTempDiff}");
                }
                tempDate = temperatureEvent.EventTimeUTC;
                tempValue = temperatureEvent.Temperature;

                showStr.Clear();
                showStr.AppendLine($"Lat: {lat:##0.0000}");
                showStr.AppendLine($"Lon: {lon:##0.0000}");
                showStr.AppendLine($"Alt: {alt:#,##0}");
                showStr.AppendLine($"Temp: {tempValue:##0.000000}");

                UpdateStarTracker();
                SetStarTrackerVisibility();
                string str = $"{lat:##0.000000}, {lon:##0.000000}, {alt:##0}, {tempDate}, {tempValue:###0.000000}, {culminationString}\n";

                currentPosition.Text = showStr.ToString();

                System.Diagnostics.Debug.WriteLine(str);
                csv.Append(str);
                File.WriteAllText("D:\\ED.csv", csv.ToString());
            }
        }

        private void OnNewEntry(HistoryEntry he)
        {
            if (current_sys == null || current_sys.Name != he.System.Name)       // changed system
            {
                current_sys = he.System;        // always there
                current_body = he.Status.BodyName;        // may be blank or null
            }
        }
        public override void ReceiveHistoryEntry(HistoryEntry he)
        {
            if (current_sys == null || current_sys.Name != he.System.Name)       // changed system
            {
                current_sys = he.System;        // always there
                current_body = he.Status.BodyName;        // may be blank or null
            }
        }

        #endregion

        #region Display

        // we determine visibility from the stored flags.
        private void SetStarTrackerVisibility()
        {
            bool visible = true;

            if (intransparent && IsSet(CtrlList.autohide))       // autohide turns off when transparent And..
            {
                if (guistate != EliteDangerousCore.UIEvents.UIGUIFocus.Focus.NoFocus)    // not on main screen
                {
                    visible = false;
                }
                            // if in mainship, or srv, or we are on foot planet, we can show
                else if ((!IsSet(CtrlList.hidewheninship) && elitemode.InFlight) ||
                         (!IsSet(CtrlList.hidewheninSRV) && elitemode.Mode == EliteDangerousCore.UIEvents.UIMode.ModeType.SRV ) ||
                         (!IsSet(CtrlList.hidewhenonfoot) && ( elitemode.Mode == EliteDangerousCore.UIEvents.UIMode.ModeType.OnFootPlanet ||
                                                                elitemode.Mode == EliteDangerousCore.UIEvents.UIMode.ModeType.OnFootInstallationInside ))
                    )
                {
                }
                else
                    visible = false;    // else off
            }

            if (intransparent && IsSet(CtrlList.hidewithnolatlong) && !position.Location.ValidPosition)     // if hide if no lat/long..
            {
                visible = false;
            }
        }

        async void UpdateData()
        {
            //if (position.Location.ValidPosition)
            //{
                current_data = current_sys != null ? await DiscoveryForm.History.StarScan.FindSystemAsync(current_sys, false) : null;
                System.Diagnostics.Debug.WriteLine($"Fetched data: {current_data}");
            //}
        }

        struct Orbit

        {
            public double SemiMajorAxis { get; }
            public double Eccentricity { get; }
            public double Inclination { get; }
            public double AscendingNode { get; }
            public double ArgOfPeriapsis { get; }
            public double MeanAnomaly { get; }
            public bool IsValid { get; }

            public Orbit(JournalScanBaryCentre bc)
            {
                IsValid = true;
                SemiMajorAxis = bc.SemiMajorAxisLS;
                Eccentricity = bc.Eccentricity;
                Inclination = bc.OrbitalInclination;
                AscendingNode = bc.AscendingNode;
                ArgOfPeriapsis = bc.Periapsis;
                MeanAnomaly = bc.MeanAnomaly;

                // Keplerize
                AscendingNode = (360.0 - AscendingNode) % 360.0;
                ArgOfPeriapsis = (360.0 - ArgOfPeriapsis) % 360.0;

                double diff = (DateTime.UtcNow - bc.EventTimeUTC).TotalSeconds;

                // this barycentre has been moved since last scan
                double orbitalPeriod = bc.OrbitalPeriod;
                MeanAnomaly += 360 * diff / orbitalPeriod;
            }

            public Orbit(StarScan.ScanNode body)
            {
                IsValid = body.ScanData.nOrbitalPeriod != null;
                SemiMajorAxis = body.ScanData.nSemiMajorAxisLS.GetValueOrDefault();
                Eccentricity = body.ScanData.nEccentricity.GetValueOrDefault();
                Inclination = body.ScanData.nOrbitalInclination.GetValueOrDefault();
                AscendingNode = body.ScanData.nAscendingNode.GetValueOrDefault();
                ArgOfPeriapsis = body.ScanData.nPeriapsis.GetValueOrDefault();
                MeanAnomaly = body.ScanData.nMeanAnomaly.GetValueOrDefault();

                // Keplerize
                AscendingNode = (360.0 - AscendingNode) % 360.0;
                ArgOfPeriapsis = (360.0 - ArgOfPeriapsis) % 360.0;

                double diff = (DateTime.UtcNow - body.ScanData.EventTimeUTC).TotalSeconds;

                // this planet has been moved since last scan
                double orbitalPeriod = body.ScanData.nOrbitalPeriod.GetValueOrDefault();
                MeanAnomaly += 360 * diff / orbitalPeriod;
            }
        }

        private double findMeanAnomaly(double e, double eccentric)
        {
            return eccentric - (e * Math.Sin(eccentric));
        }

        private double findEccentricAnomaly(double e, double mean, double epsilon = 1e-10)
        {
            mean = MathHelper.DegreesToRadians(mean);
            double e_n = mean;

            // Newton's methon to find E on given M
            double diff = findMeanAnomaly(e, e_n) - mean;
            while (Math.Abs(diff) > epsilon) {
                double e_n1 = e * Math.Sin(e_n) + mean;
                e_n = e_n1;
                diff = findMeanAnomaly(e, e_n) - mean;
            }

            return e_n;
        }

        // rotate a vector CCW along axis by theta degrees, axis direction inward (toward "observer")
        private Vector3d rotateAlong(Vector3d vector, Vector3d axis, double theta)
        {
            double theta_rad = MathHelper.DegreesToRadians(theta);
            axis.Normalize();
            Matrix4d rotation = Matrix4d.CreateFromAxisAngle(axis, theta_rad);
            return Vector3d.Transform(vector, rotation);
        }

        private Vector3d transformToParentFrameOfReference(Vector3d coords, List<JournalScan.BodyParent> parentIDs)
        {
            /*
             * inclination is calculated from parent's frame of reference, which is equator.
             * given parent body, turn it
             */

            double axialTilt = 0;
            Orbit orbit;
            if (parentIDs == null || parentIDs.Count == 0)
            {
                return coords;
            }

            var parent = parentIDs.First();

            if (parent == null || parent.BodyID == 0)
            {
                return coords;
            }

            if (parent.IsBarycentre) {
                orbit = new Orbit(parent.Barycentre);
            } else {
                StarScan.ScanNode body = allBodies[parent.BodyID];
                axialTilt = body.ScanData.nAxialTiltDeg.GetValueOrDefault();
                orbit = new Orbit(body);
            }

            coords = rotateAlong(coords, new Vector3d(0, 0, 1), orbit.ArgOfPeriapsis + orbit.AscendingNode);

            Vector3d axis = rotateAlong(new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), orbit.ArgOfPeriapsis + orbit.AscendingNode);

            Vector3d ascendingNode = rotateAlong(new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), orbit.AscendingNode);

            coords = rotateAlong(coords, axis, -axialTilt);

            coords = rotateAlong(coords, ascendingNode, -orbit.Inclination);

            return transformToParentFrameOfReference(coords, parentIDs.Skip(1).ToList());
        }

        private Vector3d fromOrbitToGlobal(Vector3d coords, Orbit orbit)
        {
            // 2. apply periapsis and ascending node
            coords = rotateAlong(coords, new Vector3d(0, 0, 1), orbit.ArgOfPeriapsis + orbit.AscendingNode);

            // 3. Calculate axis to apply inclination around, reference direction +X
            Vector3d axis = rotateAlong(new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), orbit.AscendingNode);

            // 4. rotate and apply semimajor axis
            coords = rotateAlong(coords, axis, -orbit.Inclination);

            return coords;
        }

        private Vector3d calculateCoordinates(Orbit orbit)
        {
            Vector3d coords = new Vector3d();
            if (!orbit.IsValid)
            {
                return coords;
            }
            double ecc = findEccentricAnomaly(orbit.Eccentricity, orbit.MeanAnomaly);
            
            // 1. Orbit, XY plane, periapsis +X
            coords.X = Math.Cos(ecc) - orbit.Eccentricity;
            coords.Y = Math.Sqrt(1 - orbit.Eccentricity * orbit.Eccentricity) * Math.Sin(ecc);
            coords.Z = 0;

            coords = fromOrbitToGlobal(coords, orbit);

            coords *= orbit.SemiMajorAxis;
            return coords;
        }

        // looking from body and given (lat, lon) - compute target's height in the sky
        // range: (-90; 90). Negative value - below horizon, positive value - above horizon
        private double calculateAngularHeight(StarScan.ScanNode body, StarScan.ScanNode target, double lat, double lon)
        {
            double axialTilt = body.ScanData.nAxialTiltDeg.GetValueOrDefault();
            double rotationPeriod = body.ScanData.nRotationPeriod.GetValueOrDefault(86400); // default to 1 day

            Orbit orbit = new Orbit(body);

            Vector3d body_coords = summarizeCoordinates(body);

            // Calculate angle between north pole (yet) and target coords
            Vector3d target_coords = summarizeCoordinates(target);
            Vector3d diff = target_coords - body_coords;

            Vector3d northPole = new Vector3d(0, 0, 1);
            
            Vector3d axis = rotateAlong(new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), orbit.ArgOfPeriapsis + orbit.AscendingNode);
            Vector3d ascendingNode = rotateAlong(new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), orbit.AscendingNode);
            northPole = rotateAlong(northPole, axis, -axialTilt); // 1
            northPole = rotateAlong(northPole, ascendingNode, -orbit.Inclination); // 2

            // rotate axis too
            axis = rotateAlong(axis, ascendingNode, -orbit.Inclination);

            // rotate planet
#if true
            decimal magicNumberRem = 0.0M; //stock, for data gathering
#else
            decimal magicNumber = 1_659_324_348_562.951271M; // first occurence for planets 1a - 1d
            decimal magicNumberRem = Math.Abs(magicNumber % (decimal)rotationPeriod);
#endif

            // a % b keeps sign of a, -a % b = -(a % b)

            DateTime origin = new DateTime(1970, 01, 01, 00, 00, 00);
            double revs = (DateTime.UtcNow - origin).TotalSeconds % rotationPeriod;
            revs = (revs + (double)magicNumberRem) % rotationPeriod;
            double degrees = (360 * revs) / rotationPeriod;

            degrees -= 98.63; // error correction

            Vector3d zenith = rotateAlong(northPole, axis, lat - 90);
            zenith = rotateAlong(zenith, northPole, lon + degrees);
            zenith = transformToParentFrameOfReference(zenith, body.ScanData.Parents);

            // calculate minimum/maximum body height on given latitude
            if (target.NodeType == StarScan.ScanNodeType.star) {
                // axis is perpendicular to north pole and target direction
                Vector3d northPoleGlobal = transformToParentFrameOfReference(northPole, body.ScanData.Parents);
                Vector3d axis2 = Vector3d.Normalize(Vector3d.Cross(northPoleGlobal, diff));
                Vector3d subsolarZenithMax = rotateAlong(northPoleGlobal, axis2, 90 - lat);

                double maxHeight = 90 - MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diff, subsolarZenithMax));
                double sunZenithLatitude = 90 - MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diff, northPoleGlobal));
                System.Diagnostics.Debug.WriteLine($"Maximum height of {target.FullName}: {maxHeight:##0.0000} at latitude {sunZenithLatitude:##0.000000})");
                System.Diagnostics.Debug.WriteLine($"Current height of {target.FullName}: {90 - MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diff, zenith)):##0.0000})");

                // equatorial projections
                Vector3d zenithProj = zenith - northPoleGlobal * Vector3d.Dot(northPoleGlobal, zenith);
                Vector3d zenithMaxProj = subsolarZenithMax - northPoleGlobal * Vector3d.Dot(northPoleGlobal, subsolarZenithMax);

                // actual body position, projection onto equatorial plane
                Vector3d diffProj = diff - northPole * Vector3d.Dot(northPole, diff);

                // culmination angles
                double d_culminationHigh = MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diffProj, zenithMaxProj));
                double culminationHigh = MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(zenithProj, zenithMaxProj));

                System.Diagnostics.Debug.WriteLine($"Culminations of {target.FullName}: high {culminationHigh:##0.0000}");
                // check
                {
                    Vector3d z;
                    double zangle;

                    z = rotateAlong(zenith, northPoleGlobal, culminationHigh);
                    zangle = 90 - MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diff, z));
                    if (Math.Abs(zangle - maxHeight) > 0.001)
                    {
                        culminationHigh = -culminationHigh;
                    }

                    if (rotationPeriod < 0.0)
                    {
                        // planet is rotating backwards
                        culminationHigh = -culminationHigh;
                    }
                }
                //if (target.OwnName == "A")
                {
                    double height = 90 - MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diff, zenith));
                    showStr.AppendLine($"{target.FullName} height: {height:#,##0.0000}");
                    culminationString = $"height: {height:##0.0000}, {culminationHigh:##0.000000} + {d_culminationHigh:##0.000000} = {culminationHigh + d_culminationHigh:##0.000000}";
                }
            }
            
            return MathHelper.RadiansToDegrees(Vector3d.CalculateAngle(diff, zenith));
        }

        private Vector3d summarizeCoordinates(StarScan.ScanNode body)
        {
            return summarizeCoordinates(body.BodyID.GetValueOrDefault(), body.ScanData.Parents);
        }

        private Vector3d summarizeCoordinates(int bodyID, List<JournalScan.BodyParent> parentIDs)
        {
            Vector3d coords = new Vector3d();

            if (bodyID > 0)
            {
                coords = allBodiesCoordinates[bodyID];
                coords = transformToParentFrameOfReference(coords, parentIDs);
            }

            if (parentIDs == null || parentIDs.Count == 0)
            {
                return coords;
            }

            coords += summarizeCoordinates(parentIDs.First().BodyID, parentIDs.Skip(1).ToList());
            
            return coords;
        }

        private void UpdateStarTracker()
        {
            if (current_data == null) { return; }

            if (current_body == null) { return; }

            foreach (var barycenter in current_data.Barycentres)
            {
                int id = barycenter.Value.BodyID;
                Orbit orbit = new Orbit(barycenter.Value);
                Vector3d coords = calculateCoordinates(orbit);
                allBodiesCoordinates[id] = coords;
            }

            foreach (var body in current_data.Bodies)
            {
                if (   body.NodeType == StarScan.ScanNodeType.belt
                    || body.NodeType == StarScan.ScanNodeType.beltcluster)
                {
                    // belt
                    continue;
                }
                int id = body.BodyID.GetValueOrDefault();
                Orbit orbit = new Orbit(body);
                allBodies[id] = body;

                Vector3d coords = calculateCoordinates(orbit);

                allBodiesCoordinates[id] = coords;
            }

            foreach (var body in current_data.Bodies)
            {
                if (body.FullName == current_body)
                {
                    current_body_id = body.BodyID.GetValueOrDefault();
                }
            }

            if (current_body_id == 0)
            {
                return;
            }
            StarScan.ScanNode position_body = allBodies[current_body_id];
            Vector3d position = summarizeCoordinates(position_body);

            foreach (var target_body in current_data.Bodies)
            {
                if (target_body.NodeType == StarScan.ScanNodeType.belt
                    || target_body.NodeType == StarScan.ScanNodeType.beltcluster)
                {
                    // belt
                    continue;
                }
                if (target_body.FullName != current_body)
                {
                    Vector3d target = new Vector3d();

                    target = summarizeCoordinates(target_body);

                    //double distance = (target - position).Length;
                    //System.Diagnostics.Debug.WriteLine($"Distance from {position_body.FullName} to {target_body.FullName}: {distance}");

                    double angle = calculateAngularHeight(position_body, target_body, this.lat, this.lon);
                    //System.Diagnostics.Debug.WriteLine($"{target_body.FullName} height: {90 - angle} degrees");
                }
            }
        }

#endregion

        #region UI

        protected enum CtrlList
        {
            autohide,
            hidewithnolatlong,
            hidewhenonfoot,
            hidewheninSRV,
            hidewheninship,
            clearlatlong,
        };

        private bool[] ctrlset; // holds current state of each control above

        private void PopulateCtrlList()
        {
            ctrlset = GetSettingAsCtrlSet<CtrlList>((e)=> { return e == CtrlList.autohide || e == CtrlList.hidewithnolatlong; });
        }

        private bool IsSet(CtrlList v)
        {
            return ctrlset[(int)v];
        }

        #endregion

    }
}
