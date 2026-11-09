using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace PalworldServerManager;

public partial class MainWindow
{
    private readonly CancellationTokenSource _updateCancellation = new();
    private bool _checkingForUpdates;
    private bool _windowClosed;

    protected override void OnClosed(EventArgs e)
    {
        _windowClosed = true;
        _updateCancellation.Cancel();
        _serverStatusTimer.Stop();
        base.OnClosed(e);
    }

    private async void OnCheckForUpdatesClicked(object? sender, RoutedEventArgs e)
    {
        await CheckForAppUpdatesAsync();
    }

    private async Task CheckForAppUpdatesAsync()
    {
        if (_checkingForUpdates || _windowClosed)
        {
            return;
        }

        var button = this.FindControl<Button>("CheckForUpdatesButton")!;
        var status = this.FindControl<TextBlock>("UpdateStatusText")!;
        _checkingForUpdates = true;
        button.IsEnabled = false;
        var content = (Control)Content!;
        try
        {
            var manager = new UpdateManager(new GithubSource(
                "https://github.com/mildmisery/palworld-server-management-tool",
                accessToken: null,
                prerelease: false));
            if (!manager.IsInstalled)
            {
                status.Text = "Self-updates require the packaged installer or portable release.";
                return;
            }

            status.Text = $"Checking for updates (current: {manager.CurrentVersion})...";
            var update = await manager.CheckForUpdatesAsync().WaitAsync(_updateCancellation.Token);
            if (update is null)
            {
                status.Text = $"You are up to date ({manager.CurrentVersion}).";
                return;
            }

            var version = update.TargetFullRelease.Version;
            UpdateUnsavedChangesIndicator();
            if (_unsavedChangesText!.IsVisible)
            {
                status.Text = $"Version {version} available. Save or reload settings, then check again.";
                return;
            }

            if (!await ConfirmSaveOperationAsync(
                    "Update Palworld Server Manager",
                    $"Version {version} is available (current: {manager.CurrentVersion}). " +
                    "Download the update and restart this app? The Palworld server will not be stopped " +
                    "and its settings and save files will not be changed."))
            {
                status.Text = $"Version {version} available. Check again when you are ready to update.";
                return;
            }

            if (_windowClosed)
            {
                return;
            }

            UpdateUnsavedChangesIndicator();
            if (_unsavedChangesText.IsVisible)
            {
                status.Text = "Save or reload settings before updating.";
                return;
            }

            // Prevent edits while downloading so restarting cannot discard new settings.
            content.IsEnabled = false;
            status.Text = $"Downloading version {version}...";
            await manager.DownloadUpdatesAsync(update, progress =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_windowClosed)
                    {
                        status.Text = $"Downloading version {version}: {progress}%";
                    }
                }), _updateCancellation.Token);
            _updateCancellation.Token.ThrowIfCancellationRequested();
            status.Text = $"Restarting to apply version {version}...";
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (OperationCanceledException)
        {
            if (!_windowClosed)
            {
                status.Text = "Update operation cancelled or timed out. Check again to retry.";
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException or
                InvalidOperationException or JsonException or Win32Exception or
                AcquireLockFailedException or ChecksumFailedException or NotInstalledException)
        {
            status.Text = $"Update failed: {exception.Message}";
        }
        finally
        {
            _checkingForUpdates = false;
            if (!_windowClosed)
            {
                content.IsEnabled = true;
                button.IsEnabled = true;
            }
        }
    }
}
