using System.Text;
using Discord.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpyAndScrape.config;
using SpyAndScrape.FileSystem;

namespace SpyAndScrape.Trackers;

public class DiscordUsrTrack
{
    private JFH _jsonFileHandler;
    private JCmp _comparer;
    private NotifierBot _notifierBot;
    private URequestor _requestor;
    private DiscordSocketClient _client;
    private const string ProfileFileName = "discord_user_profile.json";

    public async Task StartTrackingUsr(NotifierBot notifier)
    {
        Console.WriteLine("Got permission to start");
        _notifierBot = notifier;
        _jsonFileHandler = new JFH();
        _comparer = new JCmp();
        _requestor = new URequestor();

        if (JReader.CurrentConfig.discordTrackingUsrId == 0 || string.IsNullOrEmpty(JReader.CurrentConfig.discordTrackingToken))
        {
            Console.WriteLine("[DiscordUsrTrack] Usr ID or Token not set, profile tracking disabled");
            _notifierBot.SendBotMessage("[DiscordUsrTrack] Usr ID or Token not set, profile tracking disabled");
            return;
        }

        while (JReader.CurrentConfig.trackDiscord)
        {
            await TrackProfileAsync();

            
            await Task.Delay(new Random().Next(18000, 30000));
        }
    }

    private async Task TrackProfileAsync()
    {
        
        ulong guildId = _notifierBot.GetGuildId(JReader.CurrentConfig.generalBotLogChannelId);
        if (guildId == 0)
        {
            guildId = _notifierBot.GetGuildId(JReader.CurrentConfig.generalBotImportantChannelId);
        }
        if (guildId == 0)
        {
            Console.WriteLine("[DiscordUsrTrack] Couldnt find a valid GuildID from configured channels...");
            return;
        }

        string userId = JReader.CurrentConfig.discordTrackingUsrId.ToString();
        string url = $"https://discord.com/api/v9/users/{userId}/profile";
        var queryParams = new Dictionary<string, string>
        {
            { "with_mutual_guilds", "true" },
            { "with_mutual_friends", "true" },
            { "with_mutual_friends_count", "true" },
            { "guild_id", guildId.ToString() }
        };
        var headers = new Dictionary<string, string>
        {
            { "Authorization", JReader.CurrentConfig.discordTrackingToken },
            { "Accept", "*/*" },
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36" }
        };
        
        string responseJson = await _requestor.GetAsync(url, queryParams, headers);
        if (responseJson.StartsWith("err:"))
        {
            Console.WriteLine($"[DiscordUsrTrack] API Err: {responseJson}");
            return;
        }

        JObject fullProfile = JObject.Parse(responseJson);
        
        JObject filteredProfile = FilterJsonByLogLvl(fullProfile);
        string newJsonData = filteredProfile.ToString(Formatting.Indented);

        var (hasChanges, changes) = _comparer.CompareJson(ProfileFileName, newJsonData);

        if (hasChanges)
        {
            Console.WriteLine("[DiscordUsrTrack] changes");
            
            
            string action = changes["action"].ToString();
            if (changes.Count == 0 || action == "new" || action == "deleted")
            {
                Console.WriteLine("[DiscordUsrTrack] got cursed changes:");
                Console.WriteLine(changes.ToString());
                Console.WriteLine("[DiscordUsrTrack] Old json: \n" + fullProfile.ToString() );
                
                _jsonFileHandler.CreateOverwriteJFile(ProfileFileName, newJsonData);
                return;
            }
            
            string fChanges = await FormatChangesAsync(changes);
            if (!string.IsNullOrWhiteSpace(fChanges))
            {
                _notifierBot.SendBotMessage(fChanges, 2);
            }
            _jsonFileHandler.CreateOverwriteJFile(ProfileFileName, newJsonData);
        }
    }

    // decided to divide into 3 levels, 1 is log only important, 2 most useful, 3 all
    private JObject FilterJsonByLogLvl(JObject fullJson)
    {
        int logLevel = JReader.CurrentConfig.discordTrackingLogLevel;
        if (logLevel >= 3)
        {
            return fullJson; 
        }

        var filtered = new JObject();
        var user = fullJson["user"] as JObject;
        if (user == null) return filtered;

        // 1: basic user info
        var filteredUser = new JObject
        {
            ["username"] = user["username"],
            ["global_name"] = user["global_name"],
            ["avatar"] = user["avatar"],
            ["bio"] = user["bio"]
        };

        // 2: more detailed info
        if (logLevel >= 2)
        {
            filteredUser["primary_guild"] = user["primary_guild"];
            filteredUser["clan"] = user["clan"];
            filtered["connected_accounts"] = fullJson["connected_accounts"];
            filtered["mutual_friends"] = fullJson["mutual_friends"];
            filtered["mutual_guilds"] = fullJson["mutual_guilds"];
        }

        filtered["user"] = filteredUser;
        return filtered;
    }

    private async Task<string> FormatChangesAsync(JObject changes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Discord Profile Update Detected:**\n");

        await ParseChanges(changes, sb, "");

        return sb.ToString();
    }

    private async Task ParseChanges(JToken token, StringBuilder sb, string prefix)
    {
        if (token is JObject obj)
        {
            if (obj.ContainsKey("action")) // leaf node
            {
                string action = obj["action"].ToString();
                string fieldName = prefix.TrimEnd('.');
                
                if (action == "edited")
                {
                    sb.AppendLine($"- **{fieldName}** was changed from `{obj["oldValue"]}` to `{obj["newValue"]}`.");
                }
                
                // // NOW THIS SHOULD BE EXCLUDED
                // else if (action == "added")
                // {
                //     sb.AppendLine($"- **{fieldName}** was added: `{obj["newValue"]}`");
                // }
                // else if (action == "deleted") // kinda strange if this happens, should happen only if discord themselves delete some attributes
                // {
                //     sb.AppendLine($"- **{fieldName}** was removed. Old value was: `{obj["oldValue"]}`");
                // }
            }
            else // branch node, recurse deeper
            {
                foreach (var prop in obj.Properties())
                {
                    await ParseChanges(prop.Value, sb, $"{prefix}{prop.Name}.");
                }
            }
        }
    }
}
