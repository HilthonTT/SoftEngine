namespace SoftEngine.WinForms.Controls;

public sealed partial class SliderIn 
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing) 
    {
        if(disposing && (components is not null)) 
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent() 
    {
        this.SuspendLayout();
        // 
        // SuperSlider
        // 
        this.Name = "SuperSlider";
        this.Size = new System.Drawing.Size(552, 57);
        this.ResumeLayout(false);
    }
}
