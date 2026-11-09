using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalworldServerManager.Models;

public sealed record ServerParameter(
    string Key,
    string Category,
    string Description,
    string DefaultValue = "",
    bool IsString = false,
    IReadOnlyList<string>? Options = null);

public class ServerSettings
{
    private const string SectionName = "/Script/Pal.PalGameWorldSettings";
    private string _filePath;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public static IReadOnlyList<ServerParameter> Parameters { get; } = CreateParameters();
    public IReadOnlyDictionary<string, string> Values => _values;
    public string FilePath => _filePath;

    public ServerSettings(string filePath)
    {
        _filePath = filePath;
        foreach (var parameter in Parameters)
        {
            _values[parameter.Key] = parameter.DefaultValue;
        }
    }

    public void Load()
    {
        _values.Clear();
        foreach (var parameter in Parameters)
        {
            _values[parameter.Key] = parameter.DefaultValue;
        }

        if (!File.Exists(_filePath))
        {
            return;
        }

        var text = File.ReadAllText(_filePath);
        var section = string.Empty;

        foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            if (section.Equals(SectionName, StringComparison.OrdinalIgnoreCase) &&
                line.StartsWith("OptionSettings", StringComparison.OrdinalIgnoreCase))
            {
                var separator = line.IndexOf('=');
                if (separator >= 0)
                {
                    ReadOptionSettings(line[(separator + 1)..]);
                }
            }
            else if (section.Equals("/Script/PalWorldSettings", StringComparison.OrdinalIgnoreCase))
            {
                ReadLegacySetting(line);
            }
        }

    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var optionValues = _values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair =>
            {
                var parameter = Parameters.FirstOrDefault(item => item.Key == pair.Key);
                var value = parameter?.IsString == true ? Quote(pair.Value) : pair.Value.Trim();
                return $"{pair.Key}={value}";
            });

        File.WriteAllText(
            _filePath,
            $"[{SectionName}]{Environment.NewLine}OptionSettings=({string.Join(",", optionValues)}){Environment.NewLine}");
    }

    public void SetValue(string key, string value)
    {
        _values[key] = value;
    }

    public void SetFilePath(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
    }

    private void ReadOptionSettings(string optionSettings)
    {
        var openParen = optionSettings.IndexOf('(');
        var closeParen = optionSettings.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return;
        }

        foreach (var entry in SplitEntries(optionSettings[(openParen + 1)..closeParen]))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = entry[..separator].Trim();
            var value = entry[(separator + 1)..].Trim();
            var parameter = Parameters.FirstOrDefault(item => item.Key == key);
            _values[key] = parameter?.IsString == true ? Unquote(value) : value;
        }
    }

    private void ReadLegacySetting(string line)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        key = key switch
        {
            "UseAuth" => "bUseAuth",
            "MaxPlayers" => "ServerPlayerMaxNum",
            _ => key
        };

        if (key == "PublicIP" && (value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                                  value.Equals("False", StringComparison.OrdinalIgnoreCase)))
        {
            value = string.Empty;
        }

        var parameter = Parameters.FirstOrDefault(item => item.Key == key);
        if (parameter is not null)
        {
            _values[key] = parameter.IsString ? Unquote(value) : value;
        }
    }

    private static IEnumerable<string> SplitEntries(string value)
    {
        var start = 0;
        var depth = 0;
        var inQuotes = false;
        var escaped = false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
                {
                    yield return value[start..index].Trim();
                    start = index + 1;
                }
            }
        }

        if (start < value.Length)
        {
            yield return value[start..].Trim();
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return value;
        }

        return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static IReadOnlyList<ServerParameter> CreateParameters()
    {
        const string performance = "Performance";
        const string management = "Server management";
        const string features = "Features";
        const string balance = "Game balance";
        var boolOptions = new[] { "True", "False" };

        return new[]
        {
            new ServerParameter("BaseCampMaxNum", performance, "Total number of bases across the server.", "128"),
            new ServerParameter("BaseCampMaxNumInGuild", performance, "Maximum number of bases per guild.", "4"),
            new ServerParameter("BaseCampWorkerMaxNum", performance, "Maximum number of Pals per base.", "15"),
            new ServerParameter("ItemContainerForceMarkDirtyInterval", performance, "Container re-sync interval while its UI is open (seconds).", "1"),
            new ServerParameter("MaxBuildingLimitNum", performance, "Per-player building count cap (0 = unlimited).", "0"),
            new ServerParameter("PhysicsActiveDropItemMaxNum", performance, "Maximum dropped items that can use physics.", "-1"),
            new ServerParameter("ServerReplicatePawnCullDistance", performance, "Pal sync distance from players (cm).", "15000"),

            new ServerParameter("AdminPassword", management, "Password used to obtain administrative privileges.", IsString: true),
            new ServerParameter("bAllowClientMod", management, "Allow players with mods enabled to join.", "True", Options: boolOptions),
            new ServerParameter("bEnableBuildingPlayerUIdDisplay", management, "Display the creator's player ID on structures.", "False", Options: boolOptions),
            new ServerParameter("bIsShowJoinLeftMessage", management, "Show in-game messages when players join or leave.", "True", Options: boolOptions),
            new ServerParameter("bIsUseBackupSaveData", management, "Enable world backups.", "True", Options: boolOptions),
            new ServerParameter("ChatPostLimitPerMinute", management, "Maximum chat messages allowed per minute.", "30"),
            new ServerParameter("CrossplayPlatforms", management, "Platforms allowed to connect, e.g. (Steam,Xbox,PS5,Mac).", "(Steam,Xbox,PS5,Mac)"),
            new ServerParameter("LogFormatType", management, "Log format.", "Text", Options: new[] { "Text", "Json" }),
            new ServerParameter("PublicIP", management, "Explicitly specify the external public IP.", IsString: true),
            new ServerParameter("PublicPort", management, "External public port; does not change the listening port.", "8211"),
            new ServerParameter("RCONEnabled", management, "Enable RCON.", "False", Options: boolOptions),
            new ServerParameter("RCONPort", management, "Port number used for RCON.", "25575"),
            new ServerParameter("RESTAPIEnabled", management, "Enable the REST API.", "False", Options: boolOptions),
            new ServerParameter("RESTAPIPort", management, "Listening port for the REST API.", "8212"),
            new ServerParameter("ServerDescription", management, "Server description.", IsString: true),
            new ServerParameter("ServerName", management, "Server name.", "Default Palworld Server", IsString: true),
            new ServerParameter("ServerPassword", management, "Password required to log in to the server.", IsString: true),
            new ServerParameter("ServerPlayerMaxNum", management, "Maximum number of players who can join.", "32"),
            new ServerParameter("bUseAuth", management, "Require platform authentication to join.", "True", Options: boolOptions),

            new ServerParameter("AutoResetGuildTimeNoOnlinePlayers", features, "Offline duration before automatic guild reset (hours).", "72"),
            new ServerParameter("bAllowEnemyCampSpawnNearBaseCamp", features, "Allow enemy bases to spawn near player bases.", "False", Options: boolOptions),
            new ServerParameter("bAllowEnhanceStat_Attack", features, "Allow allocating stat points to Attack.", "True", Options: boolOptions),
            new ServerParameter("bAllowEnhanceStat_Health", features, "Allow allocating stat points to HP.", "True", Options: boolOptions),
            new ServerParameter("bAllowEnhanceStat_Stamina", features, "Allow allocating stat points to Stamina.", "True", Options: boolOptions),
            new ServerParameter("bAllowEnhanceStat_Weight", features, "Allow allocating stat points to Carry Weight.", "True", Options: boolOptions),
            new ServerParameter("bAllowEnhanceStat_WorkSpeed", features, "Allow allocating stat points to Work Speed.", "True", Options: boolOptions),
            new ServerParameter("bAllowGlobalPalboxExport", features, "Allow saving to the Global Palbox.", "True", Options: boolOptions),
            new ServerParameter("bAllowGlobalPalboxImport", features, "Allow loading from the Global Palbox.", "False", Options: boolOptions),
            new ServerParameter("bAutoResetGuildNoOnlinePlayers", features, "Delete structures and base Pals if no guild members log in.", "False", Options: boolOptions),
            new ServerParameter("bBuildAreaLimit", features, "Prevent building near structures such as fast-travel points.", "False", Options: boolOptions),
            new ServerParameter("bCharacterRecreateInHardcore", features, "Allow character recreation upon death in Hardcore mode.", "False", Options: boolOptions),
            new ServerParameter("bDisplayPvPItemNumOnWorldMap_BaseCamp", features, "Show PvP-exclusive item counts for bases on the map.", "False", Options: boolOptions),
            new ServerParameter("bDisplayPvPItemNumOnWorldMap_Player", features, "Show player locations and PvP-exclusive item counts on the map.", "False", Options: boolOptions),
            new ServerParameter("bEnableFastTravel", features, "Enable fast travel.", "True", Options: boolOptions),
            new ServerParameter("bEnableFastTravelOnlyBaseCamp", features, "Restrict fast travel to between bases.", "False", Options: boolOptions),
            new ServerParameter("bEnableInvaderEnemy", features, "Enable invaders.", "True", Options: boolOptions),
            new ServerParameter("bEnableVoiceChat", features, "Enable in-game voice chat.", "False", Options: boolOptions),
            new ServerParameter("bExistPlayerAfterLogout", features, "Leave players sleeping at their current location after logout.", "False", Options: boolOptions),
            new ServerParameter("bHardcore", features, "Enable Hardcore mode.", "False", Options: boolOptions),
            new ServerParameter("bInvisibleOtherGuildBaseCampAreaFX", features, "Show base area boundaries.", "False", Options: boolOptions),
            new ServerParameter("bIsPvP", features, "Enable PvP.", "False", Options: boolOptions),
            new ServerParameter("bIsRandomizerPalLevelRandom", features, "Fully randomize wild Pal levels.", "False", Options: boolOptions),
            new ServerParameter("bIsStartLocationSelectByMap", features, "Allow players to choose their starting location.", "False", Options: boolOptions),
            new ServerParameter("bShowPlayerList", features, "Enable the player list on the ESC menu.", "False", Options: boolOptions),
            new ServerParameter("RandomizerSeed", features, "Seed used when Pal spawn randomization is enabled."),
            new ServerParameter("RandomizerType", features, "Pal spawn randomization mode.", "None", Options: new[] { "None", "Region", "All" }),
            new ServerParameter("VoiceChatMaxVolumeDistance", features, "Distance at which voice chat volume no longer attenuates.", "3000"),
            new ServerParameter("VoiceChatZeroVolumeDistance", features, "Distance at which voice chat volume becomes zero.", "15000"),

            new ServerParameter("AdditionalDropItemNumWhenPlayerKillingInPvPMode", balance, "Quantity of the special item to drop in PvP.", "1"),
            new ServerParameter("AdditionalDropItemWhenPlayerKillingInPvPMode", balance, "ID of the special item to drop in PvP.", "PlayerDropItem"),
            new ServerParameter("bAdditionalDropItemWhenPlayerKillingInPvPMode", balance, "Drop a special item when a player is killed in PvP.", "False", Options: boolOptions),
            new ServerParameter("BlockRespawnTime", balance, "Cooldown before respawning after death (seconds).", "5"),
            new ServerParameter("bPalLost", balance, "Permanently lose Pals on death.", "False", Options: boolOptions),
            new ServerParameter("BuildObjectDamageRate", balance, "Damage multiplier to buildings.", "1.000000"),
            new ServerParameter("BuildObjectDeteriorationDamageRate", balance, "Building decay speed multiplier.", "1.000000"),
            new ServerParameter("CollectionDropRate", balance, "Gatherable item quantity multiplier.", "1.000000"),
            new ServerParameter("CollectionObjectHpRate", balance, "Gatherable object health multiplier.", "1.000000"),
            new ServerParameter("CollectionObjectRespawnSpeedRate", balance, "Gatherable object respawn interval multiplier.", "1.000000"),
            new ServerParameter("DayTimeSpeedRate", balance, "Daytime progression speed multiplier.", "1.000000"),
            new ServerParameter("DeathPenalty", balance, "Items lost on death.", "Item", Options: new[] { "None", "Item", "ItemAndEquipment", "All" }),
            new ServerParameter("DenyTechnologyList", balance, "Technology IDs to disable, e.g. (\"PALBOX\",\"RepairBench\")."),
            new ServerParameter("EnemyDropItemRate", balance, "Dropped item quantity multiplier.", "1.000000"),
            new ServerParameter("EquipmentDurabilityDamageRate", balance, "Equipment durability loss multiplier.", "1.000000"),
            new ServerParameter("ExpRate", balance, "Experience gain multiplier.", "1.000000"),
            new ServerParameter("FishingDifficultyRate", balance, "Fishing difficulty (0.1–1.0).", "1.000000"),
            new ServerParameter("GuildPlayerMaxNum", balance, "Maximum number of players per guild.", "20"),
            new ServerParameter("GuildRejoinCooldownMinutes", balance, "Guild rejoin cooldown (minutes).", "0"),
            new ServerParameter("ItemCorruptionMultiplier", balance, "Item corruption speed multiplier.", "1.000000"),
            new ServerParameter("ItemWeightRate", balance, "Item weight multiplier.", "1.000000"),
            new ServerParameter("MonsterFarmActionSpeedRate", balance, "Item production speed multiplier from grazing.", "1.000000"),
            new ServerParameter("NightTimeSpeedRate", balance, "Nighttime progression speed multiplier.", "1.000000"),
            new ServerParameter("PalAutoHPRegeneRate", balance, "Pal natural HP regeneration multiplier.", "1.000000"),
            new ServerParameter("PalAutoHpRegeneRateInSleep", balance, "Pal HP regeneration while in the Palbox multiplier.", "1.000000"),
            new ServerParameter("PalCaptureRate", balance, "Capture rate multiplier.", "1.000000"),
            new ServerParameter("PalDamageRateAttack", balance, "Damage dealt by Pals multiplier.", "1.000000"),
            new ServerParameter("PalDamageRateDefense", balance, "Damage taken by Pals multiplier.", "1.000000"),
            new ServerParameter("PalEggDefaultHatchingTime", balance, "Time to hatch a Huge Egg (hours).", "1.000000"),
            new ServerParameter("PalSpawnNumRate", balance, "Pal spawn rate; higher values can affect performance.", "1.000000"),
            new ServerParameter("PalStaminaDecreaceRate", balance, "Pal stamina depletion rate multiplier.", "1.000000"),
            new ServerParameter("PalStomachDecreaceRate", balance, "Pal hunger depletion rate multiplier.", "1.000000"),
            new ServerParameter("PlayerAutoHPRegeneRate", balance, "Player natural HP regeneration multiplier.", "1.000000"),
            new ServerParameter("PlayerAutoHpRegeneRateInSleep", balance, "Player HP regeneration while sleeping multiplier.", "1.000000"),
            new ServerParameter("PlayerDamageRateAttack", balance, "Damage dealt by players multiplier.", "1.000000"),
            new ServerParameter("PlayerDamageRateDefense", balance, "Damage taken by players multiplier.", "1.000000"),
            new ServerParameter("PlayerStaminaDecreaceRate", balance, "Player stamina depletion rate multiplier.", "1.000000"),
            new ServerParameter("PlayerStomachDecreaceRate", balance, "Player hunger depletion rate multiplier.", "1.000000"),
            new ServerParameter("RespawnPenaltyDurationThreshold", balance, "Survival-time threshold for applying the respawn cooldown multiplier (seconds).", "0"),
            new ServerParameter("RespawnPenaltyTimeScale", balance, "Multiplier applied to the respawn cooldown.", "2.000000"),
            new ServerParameter("SupplyDropSpan", balance, "Meteorite and supply drop interval (minutes).", "180")
        };
    }
}
