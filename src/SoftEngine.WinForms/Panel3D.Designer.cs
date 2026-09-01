namespace SoftEngine.WinForms;

partial class Panel3D
{

    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            DisposeRenderResources();
        }
        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
        this.DoubleBuffered = true;
        this.Name = "Panel3D";
        this.Size = new System.Drawing.Size(521, 430);
        this.ResumeLayout(false);

    }

    #endregion
}
