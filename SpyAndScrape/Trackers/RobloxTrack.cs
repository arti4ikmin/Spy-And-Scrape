// https://create.roblox.com/docs/cloud/legacy/friends/v1#/
// idk why roblox is even providing these docs... but quite useful

using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpyAndScrape.FileSystem;
using System.Text;
using SpyAndScrape.config;

#pragma warning disable 4014

namespace SpyAndScrape.Trackers;

public class RobloxTrack
{
    private JFH _jsonFileHndlr;
    private JCmp _cmpr;
    private NotifierBot _notifierBot;
    private URequestor _requestor;
    
    public async Task StartTrackingRoblox(NotifierBot notifier)
    {
        _jsonFileHndlr = new JFH();
        _cmpr = new JCmp();
        _notifierBot = notifier;
        _requestor = new URequestor();
        
        var delay = JReader.CurrentConfig.generalBotTimeout;
        if (delay < 30 && JReader.CurrentConfig.sendStartingMessageOnStartup == 0)
        {
            await Task.Delay(3000);
            _notifierBot.SendBotMessage($"WARNING: The roblox tracking is running at {delay} seconds, thats below the recommended 30 seconds, be careful. \n -# Set sendStartingMessageOnStartup to 1 to disable warning.", 2, false);
        }
        
        while (JReader.CurrentConfig.trackRoblox)
        { // I really doubted that we could make requests that frequent so had to make such delays, might reconsider the game checks tho.
            delay += RandomNumberGenerator.GetInt32(0, delay / 2);
            await Task.Delay(delay * 1000);
            TrackFriendsCount();
            await Task.Delay(delay * 500);
            TrackActivity();
            await Task.Delay(delay * 800);
            TrackActivity();
            await Task.Delay(delay * 800);
            TrackFriendsCount();
            await Task.Delay(delay * 500);
            TrackActivity();
            await Task.Delay(delay * 800);
            TrackFriends();
            delay = JReader.CurrentConfig.generalBotTimeout;
            
            // TODO: IMPROVE
        }
    }

    private async Task TrackFriendsCount()
    {
        
        if (_jsonFileHndlr.GetJContents("friendscount.json") == "" || _jsonFileHndlr.GetJContents("friendscount.json") == "{}")
        {
            _jsonFileHndlr.CreateOverwriteJFile("friendscount.json", "{\"count\": -1}");
        }
        
        string res = _jsonFileHndlr.GetJContents("friendscount.json");
        
        try
        {
            string url = $"https://friends.roblox.com/v1/users/{JReader.CurrentConfig.robloxTrackingUserId}/friends/count";
            var headers = new Dictionary<string, string>
            {
                { "Accept", "application/json" }
            };

            res = await _requestor.GetAsync(url, null, headers); // doubt that we have to use await here but idk how not to

            Console.WriteLine("friends tracked: " + res);
            
        }
        finally
        {
            var cmpRes = _cmpr.CompareJson("friendscount.json", res);
            
            if (cmpRes.Item1)
            {
                string action = cmpRes.Item2["count"]["action"].ToString();
                Console.WriteLine($"action: {action}");
                if (action != "edited") { _notifierBot.SendBotMessage("**BOT RECIVED INVALID COMPAIRSON, EXPECTED ACTION : \"EDITED\", GOT: **\n" +cmpRes.Item2.ToString(Newtonsoft.Json.Formatting.Indented)); goto Unexpectedactionjmp;}
                int oldCount = (int)cmpRes.Item2["count"]["oldValue"];
                int newCount = (int)cmpRes.Item2["count"]["newValue"];
                
                _notifierBot.SendBotMessage($"Friends count changed: {cmpRes.Item1} \n " +
                                           $"Changes: From {oldCount} **to {newCount}**" + "\n " +
                                           "Additional checks might start to find out who exactly...");
                Console.WriteLine($"Friends count changed: {cmpRes.Item1} \n Changes: \n " + cmpRes.Item2.ToString(Newtonsoft.Json.Formatting.Indented) + "\n");
                
                Unexpectedactionjmp:
                TrackFriends();
            }
            _jsonFileHndlr.CreateOverwriteJFile("friendscount.json", res);
            //_requestor.Dispose();

            
        }

    }

    // did a lil more debug for this bc it didnt work at all at the start
    private async Task TrackFriends()
    { // WARNING!!!: I DID NOT TEST WITH MORE THAN 50 FRIENDS SO IDK IF THE CURSOR WORKS

        Console.WriteLine("TrackFriends");

        if (!_jsonFileHndlr.FileExists("friendslist.json"))
        {
            Console.WriteLine("'friendslist.json' not found. Creating a new one...");
            _jsonFileHndlr.CreateOverwriteJFile("friendslist.json", "{}");
        }

        string tmpFName = "friendslisttemp.json";

        if (_jsonFileHndlr.FileExists(tmpFName))
        {
            Console.WriteLine($"tmp file '{tmpFName}' exists. Deleting...");
            _jsonFileHndlr.DeleteFile(tmpFName);
        }

        Console.WriteLine($"new temporary file: {tmpFName}");
        _jsonFileHndlr.CreateOverwriteJFile(tmpFName, "{\n \"Items\": [ \n");

        string baseUrl = $"https://friends.roblox.com/v1/users/{JReader.CurrentConfig.robloxTrackingUserId}/friends/find";

        var headers = new Dictionary<string, string>
        {
            { "Accept", "application/json" }
        };

        string? nextCursor = null;
        bool isFirstItem = true;

        do
        {

            string url = nextCursor == null ?
                $"{baseUrl}?userSort=2&limit=50" :
                $"{baseUrl}?userSort=2&cursor={nextCursor}&limit=50";

            Console.WriteLine($"fetchin data from urkl: {url}");
            string res = await _requestor.GetAsync(url, null, headers);
            Console.WriteLine($"Response received: {res.Substring(0, Math.Min(res.Length, 200))}...");
            var jRes = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(res);
            foreach (var item in jRes["PageItems"])
            {
                string serializedItem = Newtonsoft.Json.JsonConvert.SerializeObject(item);
                if (isFirstItem)
                {
                    _jsonFileHndlr.AppendToJFile(tmpFName, "\t" + serializedItem);
                    isFirstItem = false;
                }
                else
                {
                    _jsonFileHndlr.AppendToJFile(tmpFName, ", \n\t" + serializedItem);
                }
            }

            // if the target has more than 50 friens threre will be a cursor, gotta refetch then
            nextCursor = jRes["NextCursor"];
            Console.WriteLine($"NextCursor value: {nextCursor}");

            await Task.Delay(100);
            
        }
        while (!string.IsNullOrEmpty(nextCursor));

        // complete json
        _jsonFileHndlr.AppendToJFile(tmpFName, "\n ] \n}");

        var resFriend = _cmpr.CompareJson("friendslist.json", _jsonFileHndlr.GetJContents(tmpFName));
        Console.WriteLine($"cmp result: {resFriend.Item1}");
        Console.WriteLine($"cmp result: {resFriend.Item2}");

        if (resFriend.Item1)
        {
            string action = resFriend.Item2["Items"]["action"].ToString();
            Console.WriteLine($"action: {action}");
            
            // _notifierBot.SendBotMessage(result.Item2.ToString(Newtonsoft.Json.Formatting.Indented), header: false);
            
            if (action == "added")
            {
                var addedIds = resFriend.Item2["Items"]["value"]
                    .Select(item => (long)item["id"])
                    .ToArray();

                StringBuilder urlAddedUsr = new StringBuilder();
                for (int i = 0; i < addedIds.Length; i++)
                {
                    urlAddedUsr.AppendFormat("[User{0}](https://www.roblox.com/users/{1}/profile)\n", i + 1, addedIds[i]);
                }
                
                _notifierBot.SendBotMessage($"Your target **added** these to their friends: \n " +
                                           $"{urlAddedUsr}" + "\n " +
                                           " --EXPERIMENTAL FEATURE-- ");
                
                Console.WriteLine("added friends completed");
            }
            if (action == "deleted")
            {
                var removedIds = resFriend.Item2["Items"]["deletedItems"]
                    .Select(item => (long)item["id"])
                    .ToArray();

                StringBuilder urlDeletedUsr = new StringBuilder();
                for (int i = 0; i < removedIds.Length; i++)
                {
                    urlDeletedUsr.AppendFormat("[User{0}](https://www.roblox.com/users/{1}/profile)\n", i + 1, removedIds[i]);
                }
                
                _notifierBot.SendBotMessage($"Your target **removed** following people from their friends list: \n " +
                                           $"{urlDeletedUsr}" + "\n " +
                                           " --EXPERIMENTAL FEATURE-- ");
                
                Console.WriteLine("removed friends completed");
            }

            if (action == "edited")
            {
                var removedIds = resFriend.Item2["Items"]["deletedItems"]
                    .Select(item => (long)item["id"])
                    .ToArray();
                StringBuilder urlDeldUsers = new StringBuilder();
                for (int i = 0; i < removedIds.Length; i++)
                {
                    urlDeldUsers.AppendFormat("[User {0}](https://www.roblox.com/users/{1}/profile)\n", i + 1, removedIds[i]);
                }
                //////////////////////////////////////////////////////////////////
                var addedIds = resFriend.Item2["Items"]["addedItems"]
                    .Select(item => (long)item["id"])
                    .ToArray();
                StringBuilder urlAddedUsers = new StringBuilder();
                for (int i = 0; i < addedIds.Length; i++)
                {
                    urlAddedUsers.AppendFormat("[User{0}](https://www.roblox.com/users/{1}/profile)\n", i + 1, addedIds[i]);
                }
                
                _notifierBot.SendBotMessage($"Your target **removed** following people from their friends list: \n " +
                                           $"{urlDeldUsers}" + "\n " +
                                           $"And **added** these to their friends: \n " +
                                           $"{urlAddedUsers}" + "\n " +
                                           " --EXPERIMENTAL FEATURE-- ");
                
                Console.WriteLine("edited friends completed");
            }
        }

        _jsonFileHndlr.CreateOverwriteJFile("friendslist.json", _jsonFileHndlr.GetJContents(tmpFName));
        Console.WriteLine($"deleting tmp file: {tmpFName}");
        _jsonFileHndlr.DeleteFile(tmpFName);

        Console.WriteLine("TrackFriends process completed");

    }





    private async Task TrackActivity()
    {

        const string fPath = "activity.json";
        
        
        
        if (!_jsonFileHndlr.FileExists(fPath))
        {
            _jsonFileHndlr.CreateOverwriteJFile(fPath, "{\"userPresenceType\":-1}");
        }

        const string url = "https://presence.roblox.com/v1/presence/users";
        var requestBody = new
        {
            userIds = new ulong[] { JReader.CurrentConfig.robloxTrackingUserId }
        };

        string jsonBody = JsonConvert.SerializeObject(requestBody);
        var headers = new Dictionary<string, string> { { "accept", "application/json" } };

        string presenceRes = await _requestor.PostAsync(url, jsonBody, headers);
        JObject jObject = JObject.Parse(presenceRes);

        // only the userPresenceType
        var userPresence = jObject["userPresences"]?[0];
        if (userPresence == null)
        {
            Console.WriteLine("No user data found yet");
            return;
        }

        int userPresenceType = (int)userPresence["userPresenceType"];
        var minimalData = new JObject { ["userPresenceType"] = userPresenceType };

        string newJsonData = minimalData.ToString(Formatting.Indented);
        Console.WriteLine("minimal: " + newJsonData);

        var resSimple = _cmpr.CompareJson(fPath, newJsonData);

        if (resSimple.Item1)
        {
            string usrPresenceOut = userPresenceType switch
            {
                0 => "Offline",
                1 => "Online",
                2 => "In-Game",
                _ => "Unknown"
            };

            _notifierBot.SendBotMessage(
                $"Activity changed: {resSimple.Item1}\nUser is {usrPresenceOut}\n", 
                2
            );

            Console.WriteLine($"Activity changed: {resSimple.Item1}\nChanges: {resSimple.Item2.ToString(Formatting.Indented)}\n");
        }

        _jsonFileHndlr.CreateOverwriteJFile(fPath, newJsonData);

    }

}