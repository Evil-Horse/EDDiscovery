/*
 * Copyright © 2016 - 2017 EDDiscovery development team
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
 * 
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */
namespace EDDiscovery.UserControls
{
    partial class UserControlStarTracker
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.flowLayoutPanelTop = new System.Windows.Forms.FlowLayoutPanel();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.currentPosition = new ExtendedControls.ExtLabel();
            this.SuspendLayout();
            // 
            // flowLayoutPanelTop
            // 
            this.flowLayoutPanelTop.AutoSize = true;
            this.flowLayoutPanelTop.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.flowLayoutPanelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.flowLayoutPanelTop.Location = new System.Drawing.Point(0, 0);
            this.flowLayoutPanelTop.Name = "flowLayoutPanelTop";
            this.flowLayoutPanelTop.Size = new System.Drawing.Size(915, 0);
            this.flowLayoutPanelTop.TabIndex = 15;
            this.flowLayoutPanelTop.WrapContents = false;
            // 
            // toolTip
            // 
            this.toolTip.ShowAlways = true;
            // 
            // currentPosition
            // 
            this.currentPosition.AutoSize = true;
            this.currentPosition.Location = new System.Drawing.Point(4, 4);
            this.currentPosition.Name = "currentPosition";
            this.currentPosition.Size = new System.Drawing.Size(0, 13);
            this.currentPosition.TabIndex = 16;
            this.currentPosition.TextBackColor = System.Drawing.Color.Transparent;
            // 
            // UserControlStarTracker
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.currentPosition);
            this.Controls.Add(this.flowLayoutPanelTop);
            this.Name = "UserControlStarTracker";
            this.Size = new System.Drawing.Size(915, 320);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.FlowLayoutPanel flowLayoutPanelTop;
        private System.Windows.Forms.ToolTip toolTip;
        private ExtendedControls.ExtLabel currentPosition;
    }
}
