using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalworldServerManager.Models;

public sealed record WorldSave(string Id, string DirectoryPath);

public sealed class WorldSaveManager
{
    private const string SettingsSnapshotName = "PalWorldSettings.ini";
    private readonly string _activeSaveDirectory;

    public string ActiveSaveDirectory => _activeSaveDirectory;
    public string BackupsDirectory => Path.Combine(Directory.GetParent(_activeSaveDirectory)!.FullName, "Backups");
    public string DisabledDirectory => Path.Combine(Directory.GetParent(_activeSaveDirectory)!.FullName, "Disabled");

    public WorldSaveManager(string activeSaveDirectory)
    {
        _activeSaveDirectory = Path.GetFullPath(activeSaveDirectory);
    }

    public IReadOnlyList<WorldSave> GetActiveSaves()
    {
        if (!Directory.Exists(_activeSaveDirectory))
        {
            return Array.Empty<WorldSave>();
        }

        return Directory.EnumerateDirectories(_activeSaveDirectory)
            .Select(path => new WorldSave(Path.GetFileName(path), path))
            .Where(save => File.Exists(Path.Combine(save.DirectoryPath, "Level.sav")))
            .ToArray();
    }

    public IReadOnlyList<WorldSave> GetBackups()
    {
        return GetWorldDirectories(BackupsDirectory, GetOriginalWorldId);
    }

    public IReadOnlyList<WorldSave> GetDisabledSaves()
    {
        return GetWorldDirectories(DisabledDirectory, GetOriginalWorldId);
    }

    public static bool HasSettingsSnapshot(WorldSave save)
    {
        return File.Exists(Path.Combine(save.DirectoryPath, SettingsSnapshotName));
    }

    public string BackupActiveSave(string serverName, string settingsFilePath)
    {
        var activeSave = GetSingleActiveSave();
        var saveNumber = GetBackups().Count + 1;
        var backupName = CreateManagedSaveName(serverName, saveNumber, activeSave.Id);
        var backupPath = GetUniqueDirectory(BackupsDirectory, backupName);
        CopyDirectoryAtomically(activeSave.DirectoryPath, backupPath, settingsFilePath);
        return backupPath;
    }

    public string DisableActiveSave(string serverName, string settingsFilePath)
    {
        return DisableActiveSaveWithSnapshot(serverName, settingsFilePath).BackupPath;
    }

    private (string BackupPath, string DisabledPath) DisableActiveSaveWithSnapshot(
        string serverName, string settingsFilePath)
    {
        var activeSave = GetSingleActiveSave();
        var backupPath = BackupActiveSave(serverName, settingsFilePath);
        var disabledPath = GetUniqueDirectory(DisabledDirectory, Path.GetFileName(backupPath));
        Directory.CreateDirectory(DisabledDirectory);
        Directory.Move(activeSave.DirectoryPath, disabledPath);
        try
        {
            File.Copy(Path.Combine(backupPath, SettingsSnapshotName),
                Path.Combine(disabledPath, SettingsSnapshotName), overwrite: true);
        }
        catch
        {
            Directory.Move(disabledPath, activeSave.DirectoryPath);
            throw;
        }

        return (backupPath, disabledPath);
    }

    public void RestoreBackup(WorldSave backup, string settingsFilePath, string serverName,
        bool allowMissingSettings = false)
    {
        ReplaceActiveSave(backup, settingsFilePath, serverName, allowMissingSettings);
    }

    public void EnableSave(WorldSave disabledSave, string settingsFilePath, string serverName,
        bool allowMissingSettings = false)
    {
        ReplaceActiveSave(disabledSave, settingsFilePath, serverName, allowMissingSettings);
    }

    private void ReplaceActiveSave(WorldSave sourceSave, string settingsFilePath, string serverName,
        bool allowMissingSettings)
    {
        var snapshotPath = Path.Combine(sourceSave.DirectoryPath, SettingsSnapshotName);
        if (!HasSettingsSnapshot(sourceSave) && !allowMissingSettings)
        {
            throw new InvalidOperationException("This world has no settings snapshot. Confirm keeping current settings before restoring it.");
        }

        var activeSaveParent = Directory.GetParent(_activeSaveDirectory)!.FullName;
        var stagedPath = Path.Combine(activeSaveParent, $".restore-{Guid.NewGuid():N}");
        var stagedSettingsPath = $"{settingsFilePath}.restore-{Guid.NewGuid():N}";
        var currentSaves = GetActiveSaves();
        if (currentSaves.Count > 1)
        {
            throw new InvalidOperationException("More than one active world save was found; no save was changed.");
        }

        var destinationPath = Path.Combine(_activeSaveDirectory, sourceSave.Id);
        if (Directory.Exists(destinationPath) &&
            !currentSaves.Any(save => string.Equals(save.DirectoryPath, destinationPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException($"The active save directory already exists: {destinationPath}");
        }

        string? disabledPath = null;
        var worldInstalled = false;
        try
        {
            Directory.CreateDirectory(_activeSaveDirectory);
            CopyDirectoryAtomically(sourceSave.DirectoryPath, stagedPath);
            // Settings belong in the config directory, not in the active world folder.
            File.Delete(Path.Combine(stagedPath, SettingsSnapshotName));
            if (File.Exists(snapshotPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsFilePath))!);
                File.Copy(snapshotPath, stagedSettingsPath);
            }

            if (currentSaves.Count == 1)
            {
                disabledPath = DisableActiveSaveWithSnapshot(serverName, settingsFilePath).DisabledPath;
            }

            Directory.Move(stagedPath, destinationPath);
            worldInstalled = true;
            if (File.Exists(stagedSettingsPath))
            {
                File.Move(stagedSettingsPath, settingsFilePath, overwrite: true);
            }
        }
        catch
        {
            if (worldInstalled)
            {
                Directory.Move(destinationPath, stagedPath);
            }

            if (disabledPath is not null)
            {
                File.Delete(Path.Combine(disabledPath, SettingsSnapshotName));
                Directory.Move(disabledPath, currentSaves[0].DirectoryPath);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(stagedPath))
            {
                Directory.Delete(stagedPath, true);
            }

            File.Delete(stagedSettingsPath);
        }
    }

    private WorldSave GetSingleActiveSave()
    {
        var activeSaves = GetActiveSaves();
        return activeSaves.Count switch
        {
            1 => activeSaves[0],
            0 => throw new InvalidOperationException("No active world save was found."),
            _ => throw new InvalidOperationException("More than one active world save was found.")
        };
    }

    private static IReadOnlyList<WorldSave> GetWorldDirectories(
        string parentDirectory,
        Func<string, string> getWorldId)
    {
        if (!Directory.Exists(parentDirectory))
        {
            return Array.Empty<WorldSave>();
        }

        return Directory.EnumerateDirectories(parentDirectory)
            .Select(path => new WorldSave(getWorldId(path), path))
            .Where(save => File.Exists(Path.Combine(save.DirectoryPath, "Level.sav")))
            .OrderByDescending(save => Directory.GetLastWriteTimeUtc(save.DirectoryPath))
            .ToArray();
    }

    private static string GetOriginalWorldId(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath);
        const string worldIdMarker = "__worldid_";
        var markerIndex = directoryName.LastIndexOf(worldIdMarker, StringComparison.Ordinal);
        var separator = directoryName.IndexOf('_');
        var worldId = markerIndex >= 0
            ? directoryName[(markerIndex + worldIdMarker.Length)..]
            : separator >= 0 ? directoryName[(separator + 1)..] : directoryName;
        var duplicateSuffix = worldId.LastIndexOf('_');
        if (duplicateSuffix >= 0 && worldId[(duplicateSuffix + 1)..].Length == 32 &&
            worldId[(duplicateSuffix + 1)..].All(Uri.IsHexDigit))
        {
            return worldId[..duplicateSuffix];
        }

        return worldId;
    }

    private static string CreateManagedSaveName(string serverName, int saveNumber, string worldId)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeServerName = new string(serverName
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeServerName))
        {
            safeServerName = "Palworld Server";
        }

        return $"{safeServerName} {saveNumber} - ({DateTime.Now:yyyy-MM-dd HH-mm-ss})__worldid_{worldId}";
    }

    private static string GetUniqueDirectory(string parentDirectory, string name)
    {
        Directory.CreateDirectory(parentDirectory);
        var path = Path.Combine(parentDirectory, name);
        if (!Directory.Exists(path))
        {
            return path;
        }

        return Path.Combine(parentDirectory, $"{name}_{Guid.NewGuid():N}");
    }

    private static void CopyDirectoryAtomically(string sourceDirectory, string destinationDirectory,
        string? settingsFilePath = null)
    {
        var stagingDirectory = $"{destinationDirectory}.partial-{Guid.NewGuid():N}";
        try
        {
            CopyDirectory(sourceDirectory, stagingDirectory);
            if (settingsFilePath is not null)
            {
                File.Copy(settingsFilePath, Path.Combine(stagingDirectory, SettingsSnapshotName), overwrite: true);
            }

            Directory.Move(stagingDirectory, destinationDirectory);
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }

            throw;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }
}
