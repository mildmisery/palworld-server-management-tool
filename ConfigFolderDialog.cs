using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace PalworldServerManager;

internal sealed class ConfigFolderDialog : Window
{
    private readonly TextBox _folderPathBox;

    public ConfigFolderDialog(string suggestedFolder)
    {
        Title = "Confirm Palworld settings location";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _folderPathBox = new TextBox
        {
            Text = suggestedFolder,
            MinWidth = 380
        };

        var browseButton = new Button { Content = "Browse..." };
        browseButton.Click += async (_, _) => await BrowseForFolderAsync();

        var pathPanel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        pathPanel.Children.Add(_folderPathBox);
        Grid.SetColumn(browseButton, 1);
        pathPanel.Children.Add(browseButton);

        var useButton = new Button { Content = "Use this folder", IsDefault = true };
        useButton.Click += (_, _) => Close(_folderPathBox.Text);

        var cancelButton = new Button { Content = "Cancel", IsCancel = true };
        cancelButton.Click += (_, _) => Close(null);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actions.Children.Add(cancelButton);
        actions.Children.Add(useButton);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Confirm the Palworld server configuration folder. The app will save PalWorldSettings.ini in this folder.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Use this tool at your own risk. It can modify or move game settings and save data. Back up important files first; the developer is not responsible for data loss.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Foreground = Avalonia.Media.Brushes.Red,
                },
                pathPanel,
                actions
            }
        };
    }

    private async Task BrowseForFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Palworld WindowsServer configuration folder",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            _folderPathBox.Text = folders[0].Path.LocalPath;
        }
    }
}
