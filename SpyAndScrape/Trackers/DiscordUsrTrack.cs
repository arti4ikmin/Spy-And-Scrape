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

            
            await Task.Delay(new Random().Next(JReader.CurrentConfig.generalBotTimeout * 6, JReader.CurrentConfig.generalBotTimeout * 10));
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
            Console.WriteLine("[DiscordUsrTrack] changes: \n" + changes.ToString(Formatting.Indented) + "\n\nRetrived: \n" + fullProfile.ToString(Formatting.Indented));

            JToken actionToken = changes["action"];
            if (actionToken != null && actionToken.ToString() == "new")
            {
                Console.WriteLine($"[DiscordUsrTrack] New profile data detected in {ProfileFileName}");
                _notifierBot.SendBotMessage($"Initial profile data ({JReader.CurrentConfig.generalTargetName}) captured.", 1);
                _jsonFileHandler.CreateOverwriteJFile(ProfileFileName, newJsonData);
                return;
            }
            
            // if (changes.Count == 0) {
            //     Console.WriteLine("[DiscordUsrTrack] 'hasChanges' was true, but the 'changes' object is empty, should be impossible but comes to light sometimes, ts might indicate a noncontent update or an issue in JCmp. Updating file anyway");
            //     _jsonFileHandler.CreateOverwriteJFile(ProfileFileName, newJsonData);
            //     return;
            // }

            Console.WriteLine("[DiscordUsrTrack] format begin ");
            string fChanges = await FormatChangesAsync(changes);
            if (!string.IsNullOrWhiteSpace(fChanges))
            {
                _notifierBot.SendBotMessage(fChanges, JReader.CurrentConfig.discordTrackingLogLevel);
            }
            else
            {
                Console.WriteLine("[DiscordUsrTrack] No formatted changes to report, or it failed");
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
            string currentFieldName = prefix.TrimEnd('.');
            if (obj.ContainsKey("action"))
            {
                string action = obj["action"].ToString();

                if (action == "edited" && obj.ContainsKey("oldValue") && obj.ContainsKey("newValue"))
                {
                    sb.AppendLine($"- **{currentFieldName}** was changed from `{obj["oldValue"]}` to `{obj["newValue"]}`.");
                }
                else if (action == "added" && (obj.ContainsKey("newValue")/* || obj.ContainsKey("value")*/))
                {
                    sb.AppendLine($"- **{currentFieldName}** was added: `{obj["value"]}`.");
                }
                else if (action == "deleted" && (obj.ContainsKey("oldValue")/* || obj.ContainsKey("value")*/))
                {
                    sb.AppendLine($"- **{currentFieldName}** was removed. (Old val was: `{obj["value"]}`.)");
                }

                
                else if (obj.ContainsKey("addedItems") || obj.ContainsKey("deletedItems") || obj.ContainsKey("editedItems"))
                {
                    if (obj["addedItems"] is JArray added && added.Count > 0)
                    {
                        sb.AppendLine($"- In **{currentFieldName}**, items were added:");
                        foreach (var item in added) {
                            string itemDesc = item["id"]?.ToString() ?? item["name"]?.ToString() ?? item.ToString(Formatting.None);
                            sb.AppendLine($"  - Added: `{itemDesc}`");
                        }
                    }
                    if (obj["deletedItems"] is JArray deleted && deleted.Count > 0)
                    {
                        sb.AppendLine($"- In **{currentFieldName}**, items were removed:");
                         foreach (var item in deleted) {
                            string itemDesc = item["id"]?.ToString() ?? item["name"]?.ToString() ?? item.ToString(Formatting.None);
                            sb.AppendLine($"  - Removed: `{itemDesc}`");
                        }
                    }
                    if (obj["editedItems"] is JArray editedArr && editedArr.Count > 0)
                    {
                        sb.AppendLine($"- In **{currentFieldName}**, items were modified:");
                        foreach (JObject editedItemDetail in editedArr.Cast<JObject>())
                        {
                            string itemId = editedItemDetail["id"]?.ToString() ?? "Unknown ID";
                            // TODO: recursively call ParseChanges on editedItemDetail["oldValue"] vs editedItemDetail["newValue"]
                            sb.AppendLine($"  - Item `{itemId}` changed. New state: `{editedItemDetail["newValue"]?.ToString(Formatting.None)}`");
                        }
                    }
                }
            }
            else
            {
                foreach (var prop in obj.Properties())
                {
                    await ParseChanges(prop.Value, sb, $"{prefix}{prop.Name}.");
                }
            }
        }
    }
}
