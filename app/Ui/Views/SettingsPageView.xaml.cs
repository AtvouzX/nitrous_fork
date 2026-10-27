using System.Windows.Controls;
using Nitrous.Managers;
using UserControl = System.Windows.Controls.UserControl;

namespace Nitrous.Ui.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
        SettingsVersionText.Text = $"Nitrous {UpdateManager.CurrentVersion}";
    }

    public TextBlock VersionTextElement => SettingsVersionText;
}
