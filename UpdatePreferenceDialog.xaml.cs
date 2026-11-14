using System.Windows;

namespace Tricera360;

public partial class UpdatePreferenceDialog : Window
{
    public UpdatePreferenceDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => App.ApplyTitleBar(this);
    }

    private void EnableUpdates(object sender, RoutedEventArgs e) => DialogResult = true;
}
