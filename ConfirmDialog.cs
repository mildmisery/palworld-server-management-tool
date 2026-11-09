using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PalworldServerManager;

internal sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var confirmButton = new Button { Content = "Continue", IsDefault = true };
        confirmButton.Click += (_, _) => Close(true);

        var cancelButton = new Button { Content = "Cancel", IsCancel = true };
        cancelButton.Click += (_, _) => Close(false);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(confirmButton);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                actions
            }
        };
    }
}
