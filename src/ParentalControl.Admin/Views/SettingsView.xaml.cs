using System.Windows.Controls;
using System.Windows.Input;

namespace ParentalControl.Admin.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scv)
        {
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta / 2.0));
            e.Handled = true;
        }
    }
}
