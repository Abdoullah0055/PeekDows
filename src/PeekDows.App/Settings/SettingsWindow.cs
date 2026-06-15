using System.Windows.Forms;

namespace PeekDows.App.Settings;

public class SettingsWindow : Form
{
    public SettingsWindow()
    {
        Text = "PeekDows Settings";
        Width = 400;
        Height = 400;
        StartPosition = FormStartPosition.CenterScreen;
        
        var label = new Label
        {
            Text = "Settings will be here (Batch 2+)",
            AutoSize = true,
            Location = new System.Drawing.Point(10, 10)
        };
        Controls.Add(label);
    }
}
