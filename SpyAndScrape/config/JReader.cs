using System.Reflection;
using Newtonsoft.Json;
using System.Diagnostics;

namespace SpyAndScrape.config;

class JReader
{
    
    //hard coded... :skull:
    public class Config
    {
        public string generalTargetName { get; set; } = "YourTargetName";
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

    public static async Task GetStartingJsonAsync()
    {
        string cfgFPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");

        await Task.Run(() =>
        {
            if (!File.Exists(cfgFPath))
            {
                Console.WriteLine("cfg not found, creating new config file...");
                var defCfg = new Config();
                WriteConfigToFile(defCfg, cfgFPath);
            }
            else
            {
                Console.WriteLine("cfg file found, getting values...");
                string jsonContent = File.ReadAllText(cfgFPath);
                CurrentConfig = JsonConvert.DeserializeObject<Config>(jsonContent);

                //
                bool upd = ValidateAndFixConfig(CurrentConfig);

                if (upd)
                {
                    WriteConfigToFile(CurrentConfig, cfgFPath);
                    Console.WriteLine("cfg file updated with values for missing fields");
                }
            }
        });
    }

    private static bool ValidateAndFixConfig(Config cfg)
    {
        bool upd = false;

        foreach (PropertyInfo property in typeof(Config).GetProperties())
        {
            var currentValue = property.GetValue(cfg);

            if (currentValue == null || IsDefVal(currentValue))
            {
                // get the default val from a new instance of cfh and set it
                var defval = property.GetValue(new Config());
                property.SetValue(cfg, defval);
                Console.WriteLine($"updated default value for {property.Name}");
                upd = true;
            }
        }

        return upd;
    }

    private static bool IsDefVal(object? val)
    {
        if (val == null) return true; // how, why and what but it works
        Type type = val.GetType();
        return val.Equals(type.IsValueType ? Activator.CreateInstance(type) : null);
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
            Console.WriteLine($"'{key}'  does not exist in the configuration.");
            return false;
        }

        

        property.SetValue(CurrentConfig, Convert.ChangeType(value, property.PropertyType));
        string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        WriteConfigToFile(CurrentConfig, configFilePath);
        Console.WriteLine($"cfg value for '{key}' has been updated to '{value}'.");
        return true;

    }
}

