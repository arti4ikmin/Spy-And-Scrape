using System.Reflection;
using Newtonsoft.Json;
using System.Diagnostics;

namespace SpyAndScrape.config;

class JReader
{
    
    //hard coded... :skull:
    public class Config
    {
        public string generalTargetName { get; set; } = "Target";
        public string generalBotToken { get; set; } = "";
        public string generalBotDecoration { get; set; } = "https://github.com/arti4ikmin/AssetsDatabase/blob/main/silly.png?raw=true";
        public int generalBotTimeout { get; set; } = 30;
        public int sendStartingMessageOnStartup { get; set; } = 0;
        public ulong generalBotSetupChannelId { get; set; } = 0;
        public ulong generalBotLogChannelId { get; set; } = 0;
        public ulong generalBotImportantChannelId { get; set; } = 0;
        public string generalWhoToPing { get; set; } = "@here, <@&ROLEID>, <@PERSONID>";

        public bool trackDiscord { get; set; } = false;
        public string discordTrackingUsername { get; set; } = "";
        public string discordTrackingToken { get; set; } = "";
        public int discordTrackingLogLevel { get; set; } = 1;

        public bool trackRoblox { get; set; } = false;
        public ulong robloxTrackingUserId { get; set; } = 0;
    }

    public static Config CurrentConfig { get; private set; }
    public static bool IsNewCfgJustCreated { get; set; } = false;

    public static async Task GetStartingJsonAsync()
    {
        string cfgFPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");

        {
            if (!File.Exists(cfgFPath))
            {
                Console.WriteLine("cfg not found, creating new config file...");
                var defCfg = new Config();
                WriteConfigToFile(defCfg, cfgFPath);
                CurrentConfig = defCfg;
                IsNewCfgJustCreated = true;
            }
            else
            {
                Console.WriteLine("cfg file found, getting values...");
                try // ew try catch but prevents 1st startup bug
                {
                    string jsonContent = File.ReadAllText(cfgFPath);
                    CurrentConfig = JsonConvert.DeserializeObject<Config>(jsonContent);

                    if (CurrentConfig == null)
                    {
                        Console.WriteLine("cfg file was empty or invalid. Recreating with default values.");
                        CurrentConfig = new Config();
                        WriteConfigToFile(CurrentConfig, cfgFPath);
                        IsNewCfgJustCreated = true;
                    }
                    else
                    {
                        bool upd = ValidateAndFixConfig(CurrentConfig);
                        if (upd)
                        {
                            WriteConfigToFile(CurrentConfig, cfgFPath);
                            Console.WriteLine("cfg file updated with values for missing fields.");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Error deserializing config.json: {ex.Message} recreating with default values");
                    CurrentConfig = new Config();
                    WriteConfigToFile(CurrentConfig, cfgFPath);
                    IsNewCfgJustCreated = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error reading config.json: {ex.Message} recreating with default values");
                    CurrentConfig = new Config();
                    WriteConfigToFile(CurrentConfig, cfgFPath);
                    IsNewCfgJustCreated = true;
                }
            }
        };
    }

    private static bool ValidateAndFixConfig(Config cfg)
    {
        bool upd = false;
        var defCfg = new Config();

        foreach (PropertyInfo property in typeof(Config).GetProperties())
        {
            var currentValue = property.GetValue(cfg);

            if (currentValue == null || IsDefVal(currentValue, property.GetValue(defCfg)))
            {
                var defval = property.GetValue(defCfg);
                property.SetValue(cfg, defval);
                Console.WriteLine($"[ConfigValidate] Property '{property.Name}' was default/null, set to default: '{defval ?? "null"}'");
                upd = true;
            }
        }

        return upd;
    }

    private static bool IsDefVal(object? currentValue, object? defaultValue)
    {
        if (currentValue == null && defaultValue == null) return true;
        if (currentValue == null && defaultValue != null) return true;
        if (currentValue != null && defaultValue == null) return false;

        return currentValue?.Equals(defaultValue) ?? false;
    }

    private static void WriteConfigToFile(Config cfg, string cfgFilePath)
    {
        string json = JsonConvert.SerializeObject(cfg, Formatting.Indented);
        File.WriteAllText(cfgFilePath, json);
    }

    public static bool OverwriteConfigValue(string key, object value)
    {
        PropertyInfo? property = typeof(Config).GetProperty(key);
        if (property == null)
        {
            Console.WriteLine($"[ConfigOverwrite] Key '{key}' does not exist in the configuration.");
            return false;
        }

        try
        {
            object convertedValue = Convert.ChangeType(value, property.PropertyType);
            property.SetValue(CurrentConfig, convertedValue);

            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            WriteConfigToFile(CurrentConfig, configFilePath);
            Console.WriteLine($"[ConfigOverwrite] Config value for '{key}' has been updated to '{value}'.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigOverwrite] Error setting value for '{key}' to '{value}': {ex.Message}");
            return false;
        }
    }
}
