using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace PalworldServerManager;

internal sealed record SteamConfigLocation(string ConfigDirectory, string ServerRoot, string? Warning);

internal static class SteamConfigLocator
{
    private const string SteamRegistryKey = @"Software\Valve\Steam";
    private const string SteamWowRegistryKey = @"Software\WOW6432Node\Valve\Steam";

    public static SteamConfigLocation Find()
    {
        var steamRoots = GetSteamRoots();
        var warning = AddSteamLibraries(steamRoots);

        foreach (var steamRoot in steamRoots)
        {
            var serverRoot = Path.Combine(steamRoot, "steamapps", "common", "PalServer");
            if (File.Exists(Path.Combine(serverRoot, "DefaultPalWorldSettings.ini")) ||
                Directory.Exists(Path.Combine(serverRoot, "Pal")))
            {
                return new SteamConfigLocation(GetConfigDirectory(serverRoot), serverRoot, warning);
            }
        }

        var fallbackRoot = steamRoots.Find(Directory.Exists)
            ?? (steamRoots.Count > 0
                ? steamRoots[0]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        var fallbackServerRoot = Path.Combine(fallbackRoot, "steamapps", "common", "PalServer");
        return new SteamConfigLocation(GetConfigDirectory(fallbackServerRoot), fallbackServerRoot, warning);
    }

    private static List<string> GetSteamRoots()
    {
        var roots = new List<string>();
        AddRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        AddRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Steam"));

        if (OperatingSystem.IsWindows())
        {
            AddRegistrySteamRoot(roots, Registry.CurrentUser, SteamRegistryKey);
            AddRegistrySteamRoot(roots, Registry.LocalMachine, SteamWowRegistryKey);
            AddRegistrySteamRoot(roots, Registry.LocalMachine, SteamRegistryKey);
        }

        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static void AddRegistrySteamRoot(List<string> roots, RegistryKey hive, string subKey)
    {
        using var key = hive.OpenSubKey(subKey);
        var installPath = key?.GetValue("SteamPath") as string
            ?? key?.GetValue("InstallPath") as string;
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            AddRoot(roots, installPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    private static string? AddSteamLibraries(List<string> steamRoots)
    {
        string? warning = null;
        var rootsToRead = steamRoots.ToArray();
        foreach (var steamRoot in rootsToRead)
        {
            var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
            {
                continue;
            }

            try
            {
                var contents = File.ReadAllText(libraryFile);
                foreach (Match match in Regex.Matches(contents, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    AddRoot(steamRoots, match.Groups[1].Value.Replace(@"\\", @"\"));
                }

                foreach (Match match in Regex.Matches(contents, "^\\s*\"\\d+\"\\s*\"([^\"]+)\"", RegexOptions.Multiline))
                {
                    AddRoot(steamRoots, match.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            catch (IOException exception)
            {
                warning = $"Could not read Steam library list at {libraryFile}: {exception.Message}";
            }
            catch (UnauthorizedAccessException exception)
            {
                warning = $"Could not read Steam library list at {libraryFile}: {exception.Message}";
            }
        }

        return warning;
    }

    private static string GetConfigDirectory(string serverRoot)
    {
        return Path.Combine(serverRoot, "Pal", "Saved", "Config", "WindowsServer");
    }

    private static void AddRoot(List<string> roots, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!roots.Exists(root => string.Equals(root, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            roots.Add(fullPath);
        }
    }
}
