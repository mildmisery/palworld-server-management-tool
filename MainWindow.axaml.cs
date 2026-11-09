using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PalworldServerManager.Models;

namespace PalworldServerManager;

public partial class MainWindow : Window
{
    private ServerSettings? _settings;
    private readonly Dictionary<string, Control> _settingControls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, CheckBox>> _crossplayControls = new(StringComparer.Ordinal);
    private TabControl? _settingsTabs;
    private TextBlock? _statusText;
    private TextBlock? _saveFolderText;
    private TextBlock? _activeSaveText;
    private TextBlock? _serverStatusText;
    private TextBlock? _unsavedChangesText;
    private TextBox? _serverExecutableBox;
    private TextBox? _serverArgumentsBox;
    private TextBox? _rconCommandBox;
    private TextBox? _rconOutputTextBox;
    private Button? _startServerButton;
    private Button? _stopServerButton;
    private Button? _restartServerButton;
    private ListBox? _backupList;
    private ListBox? _disabledSaveList;
    private List<WorldSave> _backups = new();
    private List<WorldSave> _disabledSaves = new();
    private string? _configDirectory;
    private WorldSaveManager? _saveManager;
    private Dictionary<string, string>? _cleanSettingsSnapshot;
    private readonly DispatcherTimer _serverStatusTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3)
    };

    public MainWindow()
    {
        InitializeComponent();

        BuildSettingsTabs();
        _serverStatusTimer.Tick += (_, _) => RefreshServerStatus();
        _serverStatusTimer.Start();
        Opened += OnOpened;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _settingsTabs = this.FindControl<TabControl>("SettingsTabs");
        _statusText = this.FindControl<TextBlock>("StatusText");
        _saveFolderText = this.FindControl<TextBlock>("SaveFolderText");
        _activeSaveText = this.FindControl<TextBlock>("ActiveSaveText");
        _serverStatusText = this.FindControl<TextBlock>("ServerStatusText");
        _unsavedChangesText = this.FindControl<TextBlock>("UnsavedChangesText");
        _serverExecutableBox = this.FindControl<TextBox>("ServerExecutableBox");
        _serverArgumentsBox = this.FindControl<TextBox>("ServerArgumentsBox");
        _rconCommandBox = this.FindControl<TextBox>("RconCommandBox");
        _rconOutputTextBox = this.FindControl<TextBox>("RconOutputTextBox");
        _startServerButton = this.FindControl<Button>("StartServerButton");
        _stopServerButton = this.FindControl<Button>("StopServerButton");
        _restartServerButton = this.FindControl<Button>("RestartServerButton");
        _backupList = this.FindControl<ListBox>("BackupList");
        _disabledSaveList = this.FindControl<ListBox>("DisabledSaveList");
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            var location = SteamConfigLocator.Find();
            _serverExecutableBox!.Text = GetServerExecutable(location.ServerRoot);
            RefreshServerStatus();
            await SelectConfigFolderAsync();
            if (_settings is null)
            {
                Close();
            }
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not load configuration: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not load configuration: {exception.Message}";
        }
    }

    private async Task SelectConfigFolderAsync()
    {
        var location = SteamConfigLocator.Find();
        var dialog = new ConfigFolderDialog(_configDirectory ?? location.ConfigDirectory);
        var selectedFolder = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            if (_settings is null)
            {
                _statusText!.Text = "No configuration folder selected. Choose a folder before saving or reloading.";
            }

            return;
        }

        _configDirectory = Path.GetFullPath(selectedFolder);
        _settings = new ServerSettings(Path.Combine(_configDirectory, "PalWorldSettings.ini"));
        _saveManager = CreateSaveManager(_configDirectory);
        try
        {
            _settings.Load();
            BindToUi();
            RefreshSaveLists();
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not load configuration or save folders: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not load configuration or save folders: {exception.Message}";
        }
        if (!string.IsNullOrWhiteSpace(location.Warning))
        {
            _statusText!.Text = location.Warning;
        }
    }

    private static string GetServerExecutable(string serverRoot)
    {
        var candidates = new[]
        {
            Path.Combine(serverRoot, "PalServer.exe"),
            Path.Combine(serverRoot, "PalServer-Win64-Test-Cmd.exe"),
            Path.Combine(serverRoot, "PalServer-Win64-Shipping-Cmd.exe")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static Process[] GetPalworldProcesses()
    {
        var processNames = new[]
        {
            "PalServer",
            "PalServer-Win64-Test-Cmd",
            "PalServer-Win64-Shipping-Cmd",
            "PalServer-Win64-Shipping"
        };
        return processNames
            .SelectMany(Process.GetProcessesByName)
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToArray();
    }

    private void RefreshServerStatus()
    {
        var processes = GetPalworldProcesses();
        var isRunning = processes.Length > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        _serverStatusText!.Text = isRunning ? "Running" : "Stopped";
        _startServerButton!.IsEnabled = !isRunning;
        _stopServerButton!.IsEnabled = isRunning;
        _restartServerButton!.IsEnabled = isRunning;
    }

    private void BuildSettingsTabs()
    {
        foreach (var category in ServerSettings.Parameters.Select(parameter => parameter.Category).Distinct())
        {
            var fieldsPanel = new StackPanel { Margin = new Avalonia.Thickness(16) };
            foreach (var parameter in ServerSettings.Parameters.Where(item => item.Category == category))
            {
                fieldsPanel.Children.Add(CreateSettingRow(parameter));
            }

            var scrollViewer = new ScrollViewer
            {
                Content = fieldsPanel
            };
            _settingsTabs!.Items.Add(new TabItem { Header = category, Content = scrollViewer });
        }
    }

    private Control CreateSettingRow(ServerParameter parameter)
    {
        var details = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3
        };
        details.Children.Add(new TextBlock
        {
            Text = parameter.Key,
            FontWeight = FontWeight.Bold
        });
        details.Children.Add(new TextBlock
        {
            Text = parameter.Description,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Default: {GetDefaultDescription(parameter)}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65
        });

        Control input;
        if (parameter.Key == "CrossplayPlatforms")
        {
            var platforms = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            var checkBoxes = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
            foreach (var platform in new[] { "Steam", "Xbox", "PS5", "Mac" })
            {
                var checkBox = new CheckBox { Content = platform };
                checkBox.Click += (_, _) => UpdateUnsavedChangesIndicator();
                platforms.Children.Add(checkBox);
                checkBoxes.Add(platform, checkBox);
            }

            _crossplayControls.Add(parameter.Key, checkBoxes);
            input = platforms;
        }
        else if (parameter.Options is not null)
        {
            var comboBox = new ComboBox
            {
                MinWidth = 220,
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (var option in parameter.Options)
            {
                comboBox.Items.Add(option);
            }

            input = comboBox;
        }
        else
        {
            input = new TextBox
            {
                MinWidth = 220,
                PlaceholderText = "Enter value",
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        if (input is TextBox textBoxInput)
        {
            textBoxInput.TextChanged += (_, _) => UpdateUnsavedChangesIndicator();
        }
        else if (input is ComboBox comboBoxInput)
        {
            comboBoxInput.SelectionChanged += (_, _) => UpdateUnsavedChangesIndicator();
        }

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(
                parameter.Key == "CrossplayPlatforms" ? "*,Auto,Auto" : "*,220,Auto"),
            ColumnSpacing = 12,
            Margin = new Avalonia.Thickness(0, 0, 0, 14)
        };
        row.Children.Add(details);
        Grid.SetColumn(input, 1);
        row.Children.Add(input);

        var resetButton = new Button
        {
            Content = "Reset",
            VerticalAlignment = VerticalAlignment.Center
        };
        resetButton.Click += (_, _) => ResetSetting(parameter);
        Grid.SetColumn(resetButton, 2);
        row.Children.Add(resetButton);
        _settingControls.Add(parameter.Key, input);
        return row;
    }

    private static string GetDefaultDescription(ServerParameter parameter)
    {
        return string.IsNullOrEmpty(parameter.DefaultValue)
            ? "Empty"
            : parameter.DefaultValue;
    }

    private void ResetSetting(ServerParameter parameter)
    {
        _settings?.SetValue(parameter.Key, parameter.DefaultValue);
        var control = _settingControls[parameter.Key];
        if (control is TextBox textBox)
        {
            textBox.Text = parameter.DefaultValue;
        }
        else if (control is ComboBox comboBox)
        {
            var optionIndex = parameter.Options!.ToList().IndexOf(parameter.DefaultValue);
            comboBox.SelectedIndex = optionIndex >= 0 ? optionIndex : 0;
        }
        else if (_crossplayControls.TryGetValue(parameter.Key, out var checkBoxes))
        {
            SetCrossplaySelection(checkBoxes, parameter.DefaultValue);
        }

        UpdateUnsavedChangesIndicator();
        _statusText!.Text = $"Reset {parameter.Key} to its default.";
    }

    private void BindToUi(bool markClean = true)
    {
        foreach (var parameter in ServerSettings.Parameters)
        {
            var value = _settings!.Values[parameter.Key];
            var control = _settingControls[parameter.Key];
            if (control is TextBox textBox)
            {
                textBox.Text = value;
            }
            else if (control is ComboBox comboBox)
            {
                var optionIndex = parameter.Options!.ToList().IndexOf(value);
                var defaultIndex = parameter.Options!.ToList().IndexOf(parameter.DefaultValue);
                comboBox.SelectedIndex = optionIndex >= 0 ? optionIndex : Math.Max(defaultIndex, 0);
            }
            else if (_crossplayControls.TryGetValue(parameter.Key, out var checkBoxes))
            {
                SetCrossplaySelection(checkBoxes, value);
            }
        }

        _statusText!.Text = $"Loaded settings from {_settings!.FilePath}";
        if (markClean)
        {
            MarkSettingsClean();
        }
        else
        {
            UpdateUnsavedChangesIndicator();
        }
    }

    private void BindFromUi()
    {
        foreach (var parameter in ServerSettings.Parameters)
        {
            var control = _settingControls[parameter.Key];
            var value = control switch
            {
                TextBox textBox => textBox.Text ?? string.Empty,
                ComboBox comboBox => comboBox.SelectedItem?.ToString() ?? string.Empty,
                _ => GetCrossplayValue(parameter.Key)
            };
            _settings!.SetValue(parameter.Key, value);
        }
    }

    private string GetCrossplayValue(string key)
    {
        if (!_crossplayControls.TryGetValue(key, out var checkBoxes))
        {
            return string.Empty;
        }

        var selectedPlatforms = checkBoxes
            .Where(item => item.Value.IsChecked == true)
            .Select(item => item.Key);
        return $"({string.Join(",", selectedPlatforms)})";
    }

    private Dictionary<string, string> GetUiSettingsSnapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in ServerSettings.Parameters)
        {
            var control = _settingControls[parameter.Key];
            snapshot[parameter.Key] = control switch
            {
                TextBox textBox => textBox.Text ?? string.Empty,
                ComboBox comboBox => comboBox.SelectedItem?.ToString() ?? string.Empty,
                _ => GetCrossplayValue(parameter.Key)
            };
        }

        return snapshot;
    }

    private void MarkSettingsClean()
    {
        _cleanSettingsSnapshot = GetUiSettingsSnapshot();
        UpdateUnsavedChangesIndicator();
    }

    private void UpdateUnsavedChangesIndicator()
    {
        if (_unsavedChangesText is null || _cleanSettingsSnapshot is null)
        {
            return;
        }

        var current = GetUiSettingsSnapshot();
        _unsavedChangesText.IsVisible = current.Any(pair =>
            !_cleanSettingsSnapshot.TryGetValue(pair.Key, out var cleanValue) ||
            !string.Equals(pair.Value, cleanValue, StringComparison.Ordinal));
    }

    private static void SetCrossplaySelection(
        IReadOnlyDictionary<string, CheckBox> checkBoxes,
        string value)
    {
        var selectedPlatforms = value
            .Trim()
            .TrimStart('(')
            .TrimEnd(')')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (platform, checkBox) in checkBoxes)
        {
            checkBox.IsChecked = selectedPlatforms.Contains(platform);
        }
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settings is null)
        {
            _statusText!.Text = "Choose a configuration folder before saving.";
            return;
        }

        BindFromUi();
        try
        {
            _settings.Save();
            MarkSettingsClean();
            _statusText!.Text = $"Settings saved to {_settings.FilePath}";
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not save settings: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not save settings: {exception.Message}";
        }
    }

    private void OnReloadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settings is null)
        {
            _statusText!.Text = "Choose a configuration folder before reloading.";
            return;
        }

        try
        {
            _settings.Load();
            BindToUi();
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not reload settings: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not reload settings: {exception.Message}";
        }
    }

    private async void OnChangeFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SelectConfigFolderAsync();
    }

    private void OnLoadDefaultTemplateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settings is null)
        {
            _statusText!.Text = "Choose a Palworld configuration folder before loading settings.";
            return;
        }

        try
        {
            var templatePath = GetDefaultSettingsTemplatePath();
            if (!File.Exists(templatePath))
            {
                _statusText!.Text = $"Default settings template not found: {templatePath}";
                return;
            }

            var template = new ServerSettings(templatePath);
            template.Load();
            foreach (var parameter in ServerSettings.Parameters)
            {
                _settings.SetValue(parameter.Key, template.Values[parameter.Key]);
            }

            BindToUi(markClean: false);
            _statusText!.Text = $"Loaded defaults from {templatePath}. Save to apply them to {_settings.FilePath}.";
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not load default settings template: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not load default settings template: {exception.Message}";
        }
    }

    private async void OnUpdateDefaultTemplateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settings is null)
        {
            _statusText!.Text = "Choose a Palworld configuration folder before updating the template.";
            return;
        }

        var templatePath = GetDefaultSettingsTemplatePath();
        var existingTemplate = File.Exists(templatePath);
        var backupPath = existingTemplate
            ? $"{templatePath}.backup-{DateTime.Now:yyyyMMdd-HHmmss}"
            : null;
        var message = existingTemplate
            ? $"Overwrite the server template at:{Environment.NewLine}{templatePath}{Environment.NewLine}{Environment.NewLine}" +
              $"A copy of the current template will be saved as:{Environment.NewLine}{backupPath}{Environment.NewLine}{Environment.NewLine}" +
              "The current settings in the editor will replace the template."
            : $"Create the server template at:{Environment.NewLine}{templatePath}{Environment.NewLine}{Environment.NewLine}" +
              "The current settings in the editor will be written to this file.";

        if (!await ConfirmSaveOperationAsync("Update default settings template", message))
        {
            return;
        }

        try
        {
            BindFromUi();
            if (backupPath is not null)
            {
                File.Copy(templatePath, backupPath, overwrite: false);
            }

            var template = new ServerSettings(templatePath);
            foreach (var parameter in ServerSettings.Parameters)
            {
                template.SetValue(parameter.Key, _settings.Values[parameter.Key]);
            }

            template.Save();
            _statusText!.Text = backupPath is null
                ? $"Created default settings template at {templatePath}."
                : $"Updated default settings template. Previous template backed up to {backupPath}.";
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not update default settings template: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not update default settings template: {exception.Message}";
        }
    }

    private string GetDefaultSettingsTemplatePath()
    {
        var executable = _serverExecutableBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executable));
            if (!string.IsNullOrWhiteSpace(executableDirectory))
            {
                var candidate = Path.Combine(executableDirectory, "DefaultPalWorldSettings.ini");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_configDirectory))
        {
            var serverRoot = Path.GetFullPath(Path.Combine(_configDirectory, "..", "..", "..", ".."));
            var candidate = Path.Combine(serverRoot, "DefaultPalWorldSettings.ini");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(SteamConfigLocator.Find().ServerRoot, "DefaultPalWorldSettings.ini");
    }

    private async void OnChangeSaveFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Palworld SaveGames\\0 folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        _saveManager = new WorldSaveManager(folders[0].Path.LocalPath);
        RefreshSaveLists();
        _statusText!.Text = $"Using save folder {_saveManager.ActiveSaveDirectory}";
    }

    private async void OnLoadConfigClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Palworld settings",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("INI files") { Patterns = new[] { "*.ini" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count == 0)
            {
                return;
            }

            var selectedFilePath = files[0].Path.LocalPath;
            var loadedSettings = new ServerSettings(selectedFilePath);
            loadedSettings.Load();
            _settings = loadedSettings;
            var selectedDirectory = Path.GetDirectoryName(selectedFilePath);
            if (selectedDirectory is not null &&
                (Path.GetFileName(selectedDirectory).Equals("WindowsServer", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileName(selectedDirectory).Equals("LinuxServer", StringComparison.OrdinalIgnoreCase)))
            {
                _configDirectory = selectedDirectory;
                _saveManager = CreateSaveManager(selectedDirectory);
                RefreshSaveLists();
            }

            BindToUi();
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not load selected configuration: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not load selected configuration: {exception.Message}";
        }
    }

    private async void OnSaveAsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var result = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Palworld settings as",
            SuggestedFileName = "PalWorldSettings.ini",
            DefaultExtension = "ini",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("INI files") { Patterns = new[] { "*.ini" } }
            }
        });

        if (result is null)
        {
            return;
        }

        if (_settings is null)
        {
            _settings = new ServerSettings(result.Path.LocalPath);
        }

        _settings.SetFilePath(result.Path.LocalPath);
        BindFromUi();
        OnSaveClicked(sender, e);
    }

    private static WorldSaveManager CreateSaveManager(string configDirectory)
    {
        var configFolderName = Path.GetFileName(configDirectory);
        var activeSaveDirectory = configFolderName.Equals("WindowsServer", StringComparison.OrdinalIgnoreCase) ||
                                  configFolderName.Equals("LinuxServer", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(Path.Combine(configDirectory, "..", "..", "SaveGames", "0"))
            : Path.GetFullPath(Path.Combine(configDirectory, "Pal", "Saved", "SaveGames", "0"));
        return new WorldSaveManager(activeSaveDirectory);
    }

    private void RefreshSaveLists()
    {
        if (_saveManager is null)
        {
            _saveFolderText!.Text = "Choose a Palworld configuration folder to locate world saves.";
            _activeSaveText!.Text = "No active world save found.";
            _backupList!.Items.Clear();
            _disabledSaveList!.Items.Clear();
            return;
        }

        _saveFolderText!.Text =
            $"Active save folder: {_saveManager.ActiveSaveDirectory}{Environment.NewLine}" +
            $"Backups: {_saveManager.BackupsDirectory}{Environment.NewLine}" +
            $"Disabled saves: {_saveManager.DisabledDirectory}";

        var activeSaves = _saveManager.GetActiveSaves();
        _activeSaveText!.Text = activeSaves.Count switch
        {
            0 => "No active world save found. Palworld may create one the next time the server starts.",
            1 => $"{activeSaves[0].Id} — {activeSaves[0].DirectoryPath}",
            _ => $"Multiple active world saves found ({activeSaves.Count}); save-changing actions are disabled."
        };

        _backups = _saveManager.GetBackups().ToList();
        _backupList!.Items.Clear();
        foreach (var backup in _backups)
        {
            _backupList.Items.Add(GetManagedSaveDisplayName(backup.DirectoryPath));
        }

        _disabledSaves = _saveManager.GetDisabledSaves().ToList();
        _disabledSaveList!.Items.Clear();
        foreach (var save in _disabledSaves)
        {
            _disabledSaveList.Items.Add(GetManagedSaveDisplayName(save.DirectoryPath));
        }
    }

    private static string GetManagedSaveDisplayName(string directoryPath)
    {
        const string worldIdMarker = "__worldid_";
        var directoryName = Path.GetFileName(directoryPath);
        var markerIndex = directoryName.LastIndexOf(worldIdMarker, StringComparison.Ordinal);
        return markerIndex >= 0
            ? directoryName[..markerIndex]
            : directoryName;
    }

    private async Task<bool> ConfirmSaveOperationAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(this);
    }

    private static bool IsPalworldServerRunning()
    {
        var processes = GetPalworldProcesses();
        var isRunning = processes.Length > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return isRunning;
    }

    private bool CanManageSaves()
    {
        if (_saveManager is null)
        {
            _statusText!.Text = "Choose a Palworld configuration folder before managing saves.";
            return false;
        }

        if (IsPalworldServerRunning())
        {
            _statusText!.Text = "Stop the Palworld server before managing world saves.";
            return false;
        }

        return true;
    }

    private void OnRefreshSavesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            RefreshSaveLists();
            _statusText!.Text = "Save list refreshed.";
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not read save folders: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not read save folders: {exception.Message}";
        }
    }

    private async void OnBackupSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanManageSaves() || !await ConfirmSaveOperationAsync(
                "Back up active world",
                "Copy the complete active world folder to a timestamped backup?"))
        {
            return;
        }

        BindFromUi();
        var serverName = _settings?.Values["ServerName"] ?? "Palworld Server";
        await RunSaveOperationAsync(() =>
            $"Backup created: {GetManagedSaveDisplayName(_saveManager!.BackupActiveSave(serverName))}");
    }

    private async void OnCreateNewWorldClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanManageSaves() || _settings is null)
        {
            return;
        }

        ServerSettings template;
        string templatePath;
        try
        {
            templatePath = GetDefaultSettingsTemplatePath();
            if (!File.Exists(templatePath))
            {
                _statusText!.Text = $"Default settings template not found: {templatePath}";
                return;
            }

            template = new ServerSettings(templatePath);
            template.Load();
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not load default settings template: {exception.Message}";
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not load default settings template: {exception.Message}";
            return;
        }

        BindFromUi();
        var serverName = _settings.Values["ServerName"];
        if (!await ConfirmSaveOperationAsync(
                "Create new world",
                "The active world will be copied to Backups, then moved to Disabled. " +
                $"Settings will be reloaded from:{Environment.NewLine}{templatePath}{Environment.NewLine}{Environment.NewLine}" +
                $"The active settings file ({_settings.FilePath}) will also be reset to those template values. " +
                "Palworld will create a fresh world the next time the server starts. Continue?"))
        {
            return;
        }

        var created = await RunSaveOperationAsync(() =>
        {
            var backupPath = _saveManager!.DisableActiveSave(serverName);
            var resetSettings = new ServerSettings(_settings.FilePath);
            foreach (var parameter in ServerSettings.Parameters)
            {
                resetSettings.SetValue(parameter.Key, template.Values[parameter.Key]);
            }

            resetSettings.Save();
            return $"World disabled. Backup created: {GetManagedSaveDisplayName(backupPath)}";
        });

        if (created)
        {
            try
            {
                _settings.Load();
                BindToUi();
                _statusText!.Text = $"Created a new world. Settings reloaded from {templatePath} and saved to {_settings.FilePath}.";
            }
            catch (IOException exception)
            {
                _statusText!.Text = $"World was created, but settings could not be reloaded: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                _statusText!.Text = $"World was created, but settings could not be reloaded: {exception.Message}";
            }
        }
    }

    private async void OnDisableSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanManageSaves() || !await ConfirmSaveOperationAsync(
                "Disable active world",
                "The active world will be backed up and moved to Disabled. Continue?"))
        {
            return;
        }

        BindFromUi();
        var serverName = _settings?.Values["ServerName"] ?? "Palworld Server";
        await RunSaveOperationAsync(() =>
            $"World disabled. Backup created: {GetManagedSaveDisplayName(_saveManager!.DisableActiveSave(serverName))}");
    }

    private async void OnRestoreBackupClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanManageSaves() || _backupList!.SelectedIndex < 0 ||
            !await ConfirmSaveOperationAsync(
                "Restore backup",
                "Restore the selected backup as the active world? The current world, if any, will first be backed up and moved to Disabled."))
        {
            return;
        }

        var backup = _backups[_backupList.SelectedIndex];
        await RunSaveOperationAsync(() =>
        {
            _saveManager!.RestoreBackup(backup);
            return $"Restored world {backup.Id} from backup.";
        });
    }

    private async void OnEnableSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanManageSaves() || _disabledSaveList!.SelectedIndex < 0 ||
            !await ConfirmSaveOperationAsync(
                "Enable disabled world",
                "Make the selected disabled save active? The current world, if any, will first be backed up and moved to Disabled."))
        {
            return;
        }

        var save = _disabledSaves[_disabledSaveList.SelectedIndex];
        await RunSaveOperationAsync(() =>
        {
            _saveManager!.EnableSave(save);
            return $"Enabled world {save.Id}.";
        });
    }

    private void OnRefreshServerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshServerStatus();
    }

    private async void OnRconSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExecuteRconCommandAsync("Save");
    }

    private async void OnRconInfoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExecuteRconCommandAsync("Info");
    }

    private async void OnRconShowPlayersClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExecuteRconCommandAsync("ShowPlayers");
    }

    private async void OnRconSendClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SendRconInputAsync();
    }

    private async void OnRconCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendRconInputAsync();
        }
    }

    private async Task SendRconInputAsync()
    {
        var command = _rconCommandBox!.Text?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _rconCommandBox.Text = string.Empty;
        await ExecuteRconCommandAsync(command);
    }

    private async Task ExecuteRconCommandAsync(string command)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        AppendRconOutput($"[{timestamp}] > {command}");
        try
        {
            var client = CreateRconClient();
            var response = await client.SendCommandAsync(command, default);
            AppendRconOutput(string.IsNullOrWhiteSpace(response)
                ? $"[{timestamp}] (command completed with no response)"
                : response.TrimEnd());
        }
        catch (SocketException exception)
        {
            AppendRconOutput($"[{timestamp}] RCON connection failed: {exception.Message}");
        }
        catch (IOException exception)
        {
            AppendRconOutput($"[{timestamp}] RCON communication failed: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            AppendRconOutput($"[{timestamp}] RCON command failed: {exception.Message}");
        }
        catch (OperationCanceledException exception)
        {
            AppendRconOutput($"[{timestamp}] RCON command timed out: {exception.Message}");
        }
    }

    private ServerRconClient CreateRconClient()
    {
        if (_settings is null ||
            !_settings.Values.TryGetValue("RCONEnabled", out var rconEnabled) ||
            !bool.TryParse(rconEnabled, out var enabled) ||
            !enabled)
        {
            throw new InvalidOperationException("Enable RCON in the loaded server settings before sending commands.");
        }

        if (!_settings.Values.TryGetValue("AdminPassword", out var password) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set AdminPassword in the loaded server settings before sending commands.");
        }

        var portValue = _settings.Values.TryGetValue("RCONPort", out var configuredPort)
            ? configuredPort
            : "25575";
        if (!int.TryParse(portValue, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Invalid RCON port: {portValue}");
        }

        return new ServerRconClient("127.0.0.1", port, password);
    }

    private void AppendRconOutput(string text)
    {
        _rconOutputTextBox!.Text = string.IsNullOrEmpty(_rconOutputTextBox.Text)
            ? text
            : $"{_rconOutputTextBox.Text}{Environment.NewLine}{text}";
        _rconOutputTextBox.CaretIndex = _rconOutputTextBox.Text?.Length ?? 0;
    }

    private async void OnBrowseServerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Palworld server executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Palworld server executable") { Patterns = new[] { "PalServer*.exe" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            _serverExecutableBox!.Text = files[0].Path.LocalPath;
        }
    }

    private async void OnStartServerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await StartServerAsync();
    }

    private async void OnStopServerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await StopServerAsync();
    }

    private async void OnRestartServerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!await ConfirmSaveOperationAsync(
                "Restart Palworld server",
                "Restart the server to apply the current settings? Current settings will be saved first."))
        {
            return;
        }

        try
        {
            if (_settings is not null)
            {
                BindFromUi();
                _settings.Save();
            }

            if (await StopServerAsync())
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                await StartServerAsync();
            }
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Could not restart server: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Could not restart server: {exception.Message}";
        }
    }

    private async Task<bool> StartServerAsync()
    {
        if (IsPalworldServerRunning())
        {
            RefreshServerStatus();
            _statusText!.Text = "The Palworld server is already running.";
            return false;
        }

        var executablePath = _serverExecutableBox!.Text?.Trim();
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _statusText!.Text = "Select a valid PalServer executable before starting.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                Arguments = _serverArgumentsBox!.Text ?? string.Empty,
                UseShellExecute = true
            });
            await Task.Delay(TimeSpan.FromSeconds(2));
            RefreshServerStatus();
            _statusText!.Text = "Palworld server start requested.";
            return true;
        }
        catch (Win32Exception exception)
        {
            _statusText!.Text = $"Could not start the Palworld server: {exception.Message}";
            return false;
        }
        catch (InvalidOperationException exception)
        {
            _statusText!.Text = $"Could not start the Palworld server: {exception.Message}";
            return false;
        }
    }

    private async Task<bool> StopServerAsync()
    {
        var processes = GetPalworldProcesses();
        if (processes.Length == 0)
        {
            _statusText!.Text = "The Palworld server is not running.";
            RefreshServerStatus();
            return true;
        }

        var shutdownError = "RCON shutdown is not configured. Enable RCON and set AdminPassword to try a graceful shutdown.";
        try
        {
            if (_settings is not null &&
                _settings.Values.TryGetValue("RCONEnabled", out var rconEnabled) &&
                bool.TryParse(rconEnabled, out var enabled) && enabled &&
                _settings.Values.TryGetValue("AdminPassword", out var password) &&
                !string.IsNullOrWhiteSpace(password))
            {
                var portValue = _settings.Values.TryGetValue("RCONPort", out var configuredPort)
                    ? configuredPort
                    : "25575";
                if (!int.TryParse(portValue, out var port) || port is < 1 or > 65535)
                {
                    throw new InvalidOperationException($"Invalid RCON port: {portValue}");
                }

                var rconClient = new ServerRconClient("127.0.0.1", port, password);
                await rconClient.SendCommandAsync(
                    "Shutdown 10 Server shutting down from the server manager",
                    default);

                if (await WaitForServerExitAsync(TimeSpan.FromSeconds(30)))
                {
                    DisposeProcesses(processes);
                    RefreshServerStatus();
                    _statusText!.Text = "Palworld server shut down gracefully.";
                    return true;
                }

                shutdownError = "RCON shutdown was sent, but the server did not exit within 30 seconds.";
            }
        }
        catch (SocketException exception)
        {
            shutdownError = $"Could not connect to RCON: {exception.Message}";
        }
        catch (IOException exception)
        {
            shutdownError = $"RCON shutdown failed: {exception.Message}";
        }
        catch (InvalidOperationException exception)
        {
            shutdownError = exception.Message;
        }
        catch (OperationCanceledException exception)
        {
            shutdownError = $"RCON shutdown timed out: {exception.Message}";
        }

        if (!await ConfirmSaveOperationAsync(
                "Force stop server?",
                $"{shutdownError}{Environment.NewLine}{Environment.NewLine}Force-closing the server can corrupt or lose recent world save data. Force stop anyway?"))
        {
            DisposeProcesses(processes);
            RefreshServerStatus();
            _statusText!.Text = "Server stop cancelled.";
            return false;
        }

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception exception)
            {
                shutdownError = $"Could not force-stop Palworld server process {process.Id}: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                shutdownError = $"Access denied while stopping Palworld server process {process.Id}: {exception.Message}";
            }
        }

        DisposeProcesses(processes);
        var stopped = await WaitForServerExitAsync(TimeSpan.FromSeconds(15));
        RefreshServerStatus();
        _statusText!.Text = stopped
            ? "Palworld server was force-stopped."
            : $"{shutdownError} Could not confirm that all Palworld server processes have stopped.";
        return stopped;
    }

    private static void DisposeProcesses(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }

    private static async Task<bool> WaitForServerExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsPalworldServerRunning())
            {
                return true;
            }

            await Task.Delay(500);
        }

        return !IsPalworldServerRunning();
    }

    private async Task<bool> RunSaveOperationAsync(Func<string> operation)
    {
        try
        {
            if (IsPalworldServerRunning())
            {
                _statusText!.Text = "Stop the Palworld server before managing world saves.";
                return false;
            }

            var message = await Task.Run(operation);
            _statusText!.Text = message;
            RefreshSaveLists();
            return true;
        }
        catch (IOException exception)
        {
            _statusText!.Text = $"Save operation failed: {exception.Message}";
            RefreshSaveLists();
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            _statusText!.Text = $"Save operation failed: {exception.Message}";
            RefreshSaveLists();
            return false;
        }
        catch (InvalidOperationException exception)
        {
            _statusText!.Text = $"Save operation failed: {exception.Message}";
            RefreshSaveLists();
            return false;
        }
    }
}
