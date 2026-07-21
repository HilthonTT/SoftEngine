using System.Drawing.Printing;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
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

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rdbClassicShading = new RadioButton();
        rdbFlatShading = new RadioButton();
        rdbGouraudShading = new RadioButton();
        chkShowTriangles = new CheckBox();
        groupBox1 = new GroupBox();
        rdbNoneShading = new RadioButton();
        groupBox2 = new GroupBox();
        chkShowAxes = new CheckBox();
        chkShowXZGrid = new CheckBox();
        chkShowBackFacesCulling = new CheckBox();
        lstDemos = new ListBox();
        groupBox5 = new GroupBox();
        toolTip1 = new ToolTip(components);
        groupBox6 = new GroupBox();
        panel3D1 = new Panel3D();
        groupBox1.SuspendLayout();
        groupBox2.SuspendLayout();
        groupBox5.SuspendLayout();
        groupBox6.SuspendLayout();
        SuspendLayout();
        // 
        // rdbClassicShading
        // 
        rdbClassicShading.AutoSize = true;
        rdbClassicShading.Location = new Point(18, 48);
        rdbClassicShading.Margin = new Padding(4, 3, 4, 3);
        rdbClassicShading.Name = "rdbClassicShading";
        rdbClassicShading.Size = new Size(61, 19);
        rdbClassicShading.TabIndex = 1;
        rdbClassicShading.TabStop = true;
        rdbClassicShading.Text = "Classic";
        rdbClassicShading.UseVisualStyleBackColor = true;
        // 
        // rdbFlatShading
        // 
        rdbFlatShading.AutoSize = true;
        rdbFlatShading.Location = new Point(18, 75);
        rdbFlatShading.Margin = new Padding(4, 3, 4, 3);
        rdbFlatShading.Name = "rdbFlatShading";
        rdbFlatShading.Size = new Size(44, 19);
        rdbFlatShading.TabIndex = 2;
        rdbFlatShading.TabStop = true;
        rdbFlatShading.Text = "Flat";
        rdbFlatShading.UseVisualStyleBackColor = true;
        // 
        // rdbGouraudShading
        // 
        rdbGouraudShading.AutoSize = true;
        rdbGouraudShading.Location = new Point(18, 102);
        rdbGouraudShading.Margin = new Padding(4, 3, 4, 3);
        rdbGouraudShading.Name = "rdbGouraudShading";
        rdbGouraudShading.Size = new Size(71, 19);
        rdbGouraudShading.TabIndex = 4;
        rdbGouraudShading.TabStop = true;
        rdbGouraudShading.Text = "Gouraud";
        rdbGouraudShading.UseVisualStyleBackColor = true;
        // 
        // chkShowTriangles
        // 
        chkShowTriangles.AutoSize = true;
        chkShowTriangles.Location = new Point(18, 22);
        chkShowTriangles.Margin = new Padding(4, 3, 4, 3);
        chkShowTriangles.Name = "chkShowTriangles";
        chkShowTriangles.Size = new Size(73, 19);
        chkShowTriangles.TabIndex = 3;
        chkShowTriangles.Text = "Triangles";
        chkShowTriangles.UseVisualStyleBackColor = true;
        // 
        // groupBox1
        // 
        groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        groupBox1.Controls.Add(rdbNoneShading);
        groupBox1.Controls.Add(rdbGouraudShading);
        groupBox1.Controls.Add(rdbClassicShading);
        groupBox1.Controls.Add(rdbFlatShading);
        groupBox1.Location = new Point(1024, 146);
        groupBox1.Margin = new Padding(4, 3, 4, 3);
        groupBox1.Name = "groupBox1";
        groupBox1.Padding = new Padding(4, 3, 4, 3);
        groupBox1.Size = new Size(158, 131);
        groupBox1.TabIndex = 4;
        groupBox1.TabStop = false;
        groupBox1.Text = "Shading";
        // 
        // rdbNoneShading
        // 
        rdbNoneShading.AutoSize = true;
        rdbNoneShading.Location = new Point(18, 22);
        rdbNoneShading.Margin = new Padding(4, 3, 4, 3);
        rdbNoneShading.Name = "rdbNoneShading";
        rdbNoneShading.Size = new Size(54, 19);
        rdbNoneShading.TabIndex = 0;
        rdbNoneShading.TabStop = true;
        rdbNoneShading.Text = "None";
        rdbNoneShading.UseVisualStyleBackColor = true;
        // 
        // groupBox2
        // 
        groupBox2.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        groupBox2.Controls.Add(chkShowAxes);
        groupBox2.Controls.Add(chkShowXZGrid);
        groupBox2.Controls.Add(chkShowBackFacesCulling);
        groupBox2.Controls.Add(chkShowTriangles);
        groupBox2.Location = new Point(1024, 14);
        groupBox2.Margin = new Padding(4, 3, 4, 3);
        groupBox2.Name = "groupBox2";
        groupBox2.Padding = new Padding(4, 3, 4, 3);
        groupBox2.Size = new Size(158, 126);
        groupBox2.TabIndex = 5;
        groupBox2.TabStop = false;
        groupBox2.Text = "Display";
        // 
        // chkShowAxes
        // 
        chkShowAxes.AutoSize = true;
        chkShowAxes.Location = new Point(18, 97);
        chkShowAxes.Margin = new Padding(4, 3, 4, 3);
        chkShowAxes.Name = "chkShowAxes";
        chkShowAxes.Size = new Size(50, 19);
        chkShowAxes.TabIndex = 8;
        chkShowAxes.Text = "Axes";
        chkShowAxes.UseVisualStyleBackColor = true;
        // 
        // chkShowXZGrid
        // 
        chkShowXZGrid.AutoSize = true;
        chkShowXZGrid.Location = new Point(18, 72);
        chkShowXZGrid.Margin = new Padding(4, 3, 4, 3);
        chkShowXZGrid.Name = "chkShowXZGrid";
        chkShowXZGrid.Size = new Size(64, 19);
        chkShowXZGrid.TabIndex = 7;
        chkShowXZGrid.Text = "XZ grid";
        chkShowXZGrid.UseVisualStyleBackColor = true;
        // 
        // chkShowBackFacesCulling
        // 
        chkShowBackFacesCulling.AutoSize = true;
        chkShowBackFacesCulling.Location = new Point(18, 47);
        chkShowBackFacesCulling.Margin = new Padding(4, 3, 4, 3);
        chkShowBackFacesCulling.Name = "chkShowBackFacesCulling";
        chkShowBackFacesCulling.Size = new Size(120, 19);
        chkShowBackFacesCulling.TabIndex = 6;
        chkShowBackFacesCulling.Text = "Back faces culling";
        chkShowBackFacesCulling.UseVisualStyleBackColor = true;
        // 
        // lstDemos
        // 
        lstDemos.BorderStyle = BorderStyle.None;
        lstDemos.Dock = DockStyle.Fill;
        lstDemos.IntegralHeight = false;
        lstDemos.Location = new Point(9, 25);
        lstDemos.Margin = new Padding(4, 3, 4, 3);
        lstDemos.Name = "lstDemos";
        lstDemos.Size = new Size(169, 567);
        lstDemos.TabIndex = 8;
        toolTip1.SetToolTip(lstDemos, "Use double click to select");
        // 
        // groupBox5
        // 
        groupBox5.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        groupBox5.Controls.Add(lstDemos);
        groupBox5.Location = new Point(14, 14);
        groupBox5.Margin = new Padding(4, 3, 4, 3);
        groupBox5.Name = "groupBox5";
        groupBox5.Padding = new Padding(9);
        groupBox5.Size = new Size(187, 601);
        groupBox5.TabIndex = 9;
        groupBox5.TabStop = false;
        groupBox5.Text = "Worlds (select=2x click)";
        // 
        // toolTip1
        // 
        toolTip1.IsBalloon = true;
        // 
        // groupBox6
        // 
        groupBox6.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        groupBox6.Controls.Add(panel3D1);
        groupBox6.Location = new Point(205, 14);
        groupBox6.Margin = new Padding(0);
        groupBox6.Name = "groupBox6";
        groupBox6.Padding = new Padding(9);
        groupBox6.Size = new Size(815, 601);
        groupBox6.TabIndex = 10;
        groupBox6.TabStop = false;
        groupBox6.Text = "Camera view";
        // 
        // panel3D1
        // 
        panel3D1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        panel3D1.BackColor = Color.WhiteSmoke;
        panel3D1.BackgroundImageLayout = ImageLayout.None;
        panel3D1.Location = new Point(13, 22);
        panel3D1.Margin = new Padding(5, 3, 5, 3);
        panel3D1.Name = "panel3D1";
        panel3D1.Size = new Size(788, 564);
        panel3D1.TabIndex = 0;
        // 
        // MainScreen
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1195, 629);
        Controls.Add(groupBox6);
        Controls.Add(groupBox5);
        Controls.Add(groupBox2);
        Controls.Add(groupBox1);
        Margin = new Padding(4, 3, 4, 3);
        MinimumSize = new Size(505, 379);
        Name = "MainScreen";
        Text = "Soft3D4Net";
        groupBox1.ResumeLayout(false);
        groupBox1.PerformLayout();
        groupBox2.ResumeLayout(false);
        groupBox2.PerformLayout();
        groupBox5.ResumeLayout(false);
        groupBox6.ResumeLayout(false);
        ResumeLayout(false);

    }

    #endregion

    private Panel3D panel3D1;
    private System.Windows.Forms.RadioButton rdbGouraudShading;
    private System.Windows.Forms.RadioButton rdbFlatShading;
    private System.Windows.Forms.RadioButton rdbClassicShading;
    private System.Windows.Forms.CheckBox chkShowTriangles;
    private System.Windows.Forms.GroupBox groupBox1;
    private System.Windows.Forms.GroupBox groupBox2;
    private System.Windows.Forms.CheckBox chkShowBackFacesCulling;
    private System.Windows.Forms.ListBox lstDemos;
    private System.Windows.Forms.GroupBox groupBox5;
    private System.Windows.Forms.RadioButton rdbNoneShading;
    private System.Windows.Forms.ToolTip toolTip1;
    private System.Windows.Forms.GroupBox groupBox6;
    private System.Windows.Forms.CheckBox chkShowXZGrid;
    private System.Windows.Forms.CheckBox chkShowAxes;
}
