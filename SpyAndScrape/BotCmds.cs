using Discord;
using Discord.WebSocket;
using System.Text;
using SpyAndScrape.config;
using System.Diagnostics;
using System.Reflection;

#pragma warning disable 4014

namespace SpyAndScrape
{
    public class BotCmds
    {
        private readonly DiscordSocketClient _client;
        private readonly NotifierBot _notifierBot;
        private readonly NotifierBot _notifierBotInstance;
        
        public BotCmds(DiscordSocketClient client, NotifierBot notifierBotInstance)
        {
            _client = client;
            _notifierBot = notifierBotInstance;
            _notifierBotInstance = notifierBotInstance;
            
            _client.Ready += OnReadyAsync;
            _client.SlashCommandExecuted += OnSlashCommandExecutedAsync;
            _client.SelectMenuExecuted += CfgMenuHandler; 
            _client.ModalSubmitted += ModalSubmitted;
        }

        private async Task OnReadyAsync()
        {
            Console.WriteLine("Bot is ready, doing commands...");
            foreach (var guild in _client.Guilds)
            {
                var helloCommand = new SlashCommandBuilder()
                    .WithName("startinfo")
                    .WithDescription("Starts booting up the bot. Gives additional info");
                
                var configChangeCommand = new SlashCommandBuilder()
                    .WithName("configchangeold")
                    .WithDescription("Change configuration settings in the bot (OLD, USE ONLY IF NEW DOESNT WORK)")
                    .AddOption("setting", ApplicationCommandOptionType.String, "Name of the setting to change", isRequired: true)
                    .AddOption("value", ApplicationCommandOptionType.String, "New value for the setting", isRequired: true);

                var configChangeTab = new SlashCommandBuilder()
                    .WithName("configchange")
                    .WithDescription("Change configuration settings in the bot");
                
                var timeOutSelf = new SlashCommandBuilder()
                    .WithName("timeoutself")
                    .WithDescription(
                        "Time out the bot, so it doesnt get annoying sometimes... (Will reset on restart.)")
                    .AddOption("time", ApplicationCommandOptionType.String,"Time the bot stays in timeout... (e.g. 1m, 15m, 1h, 48h)");
                
                    
                var debugCommand = new SlashCommandBuilder()
                    .WithName("debug")
                    .WithDescription("Sends all the output in the console, useful for finding errors.");
                
                var listConfigCommand = new SlashCommandBuilder()
                    .WithName("listconfig")
                    .WithDescription("Lists cfg settings and their values WARNING!: THIS WILL ALSO LIST API KEYS DO NOT EXECUTE IN PUBLIC");
                
                var restart = new SlashCommandBuilder()
                    .WithName("restart")
                    .WithDescription("Restarts the running program, use when you update config while running.");


                try
                {
                    await guild.CreateApplicationCommandAsync(helloCommand.Build());
                    await guild.CreateApplicationCommandAsync(configChangeCommand.Build());
                    await guild.CreateApplicationCommandAsync(configChangeTab.Build());
                    await guild.CreateApplicationCommandAsync(debugCommand.Build());
                    await guild.CreateApplicationCommandAsync(timeOutSelf.Build());
                    await guild.CreateApplicationCommandAsync(listConfigCommand.Build());
                    await guild.CreateApplicationCommandAsync(restart.Build());
                    Console.WriteLine($"cmds created in: {guild.Name}");

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }

        public async Task OnSlashCommandExecutedAsync(SocketSlashCommand cmd)
        {
            if (cmd.CommandName == "startinfo")
            {
                
                var embedBuilder = new EmbedBuilder()
                    .WithTitle("Welcome to the Stalk and Spy Bot!")
                    .WithDescription(
                        "For a better understanding of the bot please read: \n[Click here](https://github.com/arti4ikmin/AssetsDatabase/blob/main/image0.jpg?raw=true)") //TODO: UPDATE STRING FR
                    .WithColor(new Discord.Color(
                        5814783)) // clr is a decimal value (5814783 is the decimal so called 'equivalent' of the hex color - yes I am speaking in great English)
                    .AddField("Commands:",
                        "Following commands are available right now:\n** /startinfo ** - Starts the welcome message as well as some information.\n** /configchange ** - Edits the config.json through the bot by overwriting. \n ** /listconfig ** - Lists all config settings and their current values. \n ** /debug ** - Saves the full console output and sends it as a file. \n **/timeoutself** Times the bots logging out for specified time, if it gets annoying \n")
                    .WithFooter(
                        "Users must adhere to all applicable laws and regulations when using the Software. The Software must not be used for any illegal activities, including but not limited to stalking or harassment. \n")
                    .WithThumbnailUrl(JReader.CurrentConfig.generalBotDecoration);
                
                await _notifierBot.SendMsgEmbed(embedBuilder, cmd.Channel.Id);
                await cmd.RespondAsync("Processing...");
            }
            
            if (cmd.CommandName == "configchangeold")
            {
                // Get the arguments
                var setting = cmd.Data.Options.FirstOrDefault(o => o.Name == "setting")?.Value?.ToString();
                var value = cmd.Data.Options.FirstOrDefault(o => o.Name == "value")?.Value?.ToString();

                if (string.IsNullOrEmpty(setting) || string.IsNullOrEmpty(value))
                {
                    await cmd.RespondAsync("Invalid args! Please provide all..");
                    return;
                }

                var suc = JReader.OverwriteConfigValue(setting, value);
        
                if (suc)
                {
                    await cmd.RespondAsync($"Successfully changed `{setting}` to `{value}`.");
                    await JReader.GetStartingJsonAsync();
                }
                else
                {
                    await cmd.RespondAsync($"Failed to change the config for `{setting}`.");
                }
            }

            if (cmd.CommandName == "configchange")
            { // TODO: automate this ngl
                var menuBuilder = new SelectMenuBuilder()
                    .WithPlaceholder("Select to edit config")
                    .WithCustomId("cfgManager")
                    .WithMinValues(1)
                    .WithMaxValues(1)
                    .AddOption("Target Name", "generalTargetName", "Defines the name of the target (purely decorative)")
                    .AddOption("Bot Decoration", "generalBotDecoration", "Manages the default bot decoration (silly cat by default)")
                    .AddOption("Bot Timeout", "generalBotTimeout", "Specifies the time the bot is resting (in seconds)")
                    .AddOption("Startup Message", "sendStartingMessageOnStartup", "Determines if the bot sends a starting message upon startup")
                    .AddOption("Bot Setup Channel ID", "generalBotSetupChannelId", "Channel ID used for bot setup operations")
                    .AddOption("Bot Log Channel ID", "generalBotLogChannelId", "Channel ID where bot logs are sent")
                    .AddOption("Important Channel ID", "generalBotImportantChannelId", "Channel ID for sending important notifications")
                    .AddOption("Who to Ping", "generalWhoToPing", "Defines who the bot should ping for notifications")
                    .AddOption("Track Discord Activity", "trackDiscord", "true or false tracking of Discord messages (more soon)")
                    .AddOption("Discord Tracking Username", "discordTrackingUsername", "Username used for tracking Discord activity")
                    .AddOption("Discord Tracking Token", "discordTrackingToken", "Token for authenticating Discord tracking")
                    .AddOption("Discord Log Level", "discordTrackingLogLevel", "Defines the log level for Discord tracking (also determines if ping)")
                    .AddOption("Track Roblox Activity", "trackRoblox", "Enables or disables tracking of Roblox activity")
                    .AddOption("Roblox Tracking User ID", "robloxTrackingUserId", "User ID used for tracking Roblox activity");



                var builder = new ComponentBuilder()
                    .WithSelectMenu(menuBuilder);
                
                _client.SelectMenuExecuted += CfgMenuHandler;
                
                await cmd.RespondAsync("\tPlease select the item to change in your config: ", components: builder.Build());
            }
            
            if (cmd.CommandName == "debug")
            {
                const string filePath = "console_output.txt";
                const string tempFilePath = "temp_console_output.txt";

                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Copy(filePath, tempFilePath, overwrite: true); // copy, cuz we catch "used by another program"

                        var channel = cmd.Channel;
                        await cmd.RespondAsync("File should be sent shortly");
                        await channel.SendFileAsync(tempFilePath);

                        File.Delete(tempFilePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Failed to send file: {ex.Message}");
                        await cmd.RespondAsync("Failed to send the file.");
                    }
                }
                else
                {
                    await cmd.RespondAsync("No debug log file found.");
                }
            }

            if (cmd.CommandName == "timeoutself")
            {
                var time = cmd.Data.Options.FirstOrDefault(o => o.Name == "time")?.Value?.ToString();
    
                if (!string.IsNullOrEmpty(time))
                {
                    int timeMin = 0;
        
                    if (time.EndsWith('m'))
                    {
                        if (int.TryParse(time.Substring(0, time.Length - 1), out timeMin))
                        {
                            // dc timestamp
                            DateTime timeoutTime = DateTime.Now.AddMinutes(timeMin);
                            long unixTimestamp = ((DateTimeOffset)timeoutTime).ToUnixTimeSeconds();
                            string discordTimestamp = $"<t:{unixTimestamp}:R>";

                            await cmd.RespondAsync($"Bot will be timed out, left: {discordTimestamp}, restarting the program clears the timeout, it will still respond to commands. \nIf you are annoyed by pings, change the \"generalWhoToPing\" config. Tracking of services will double the delay, input will still be given to console and logs.");
                            
                            
                            _notifierBot.PauseMessages(timeMin);
                        }
                        else
                        {
                            await cmd.RespondAsync("Invalid time format for minutes.");
                        }
                    }
                    else if (time.EndsWith('h'))
                    {
                        if (int.TryParse(time.Substring(0, time.Length - 1), out int hours))
                        {
                            timeMin = hours * 60;
                            _notifierBot.PauseMessages(timeMin);
                        }
                        else
                        {
                            await cmd.RespondAsync("Invalid time format for hours.");
                        }
                    }
                    else
                    {
                        await cmd.RespondAsync("Invalid time format. Use 1m, 10m, 1h - m stands for minutes, h for hours, DO NOT COMBINE");
                    }
                }
                else
                {
                    await cmd.RespondAsync("No time specified.");
                }
            }

            
            if (cmd.CommandName == "listconfig")
            {
                var config = JReader.CurrentConfig;
                var configDetails = new StringBuilder();

                // reflection to get all public properties of the cfg class
                foreach (var property in config.GetType().GetProperties())
                {
                    var propertyName = property.Name;
                    var propertyValue = property.GetValue(config);
                    configDetails.AppendLine($"*{propertyName}:* || {propertyValue} ||");
                }
                configDetails.AppendLine("-# For more information about each, see [here](https://github.com/arti4ikmin/AssetsDatabase)");
                var configText = configDetails.ToString();
                await cmd.RespondAsync($"Here are the current bot configurations:\n");
                _notifierBot.SendBotMessage($"{configText}\n", header: false);
                // TODO: UPDATE STRING FR
            }

            if (cmd.CommandName == "restart")
            {
                cmd.RespondAsync("Trying to restart the program, await.(You should get a message in a few seconds, if not the app broke :( )");
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(exePath, "delay");
                Program.OnProcessExit(null, EventArgs.Empty);
                Environment.Exit(0);
            }

        }
        
        public async Task CfgMenuHandler(SocketMessageComponent arg)
        {
            if (arg.Data.CustomId == "cfgManager")
            {
                var selectedKey = arg.Data.Values.First();
                var mb = new ModalBuilder()
                    .WithTitle($"Update Config: {selectedKey}")
                    .WithCustomId($"updateConfig_{selectedKey}")
                    .AddTextInput("Enter the new value:", "inputValue", placeholder: "Type your value here");

                _client.ModalSubmitted += ModalSubmitted;
                await arg.RespondWithModalAsync(mb.Build());
                // await arg.Message.DeleteAsync();
                // maybe auto msgs cleanup soon
            }
        }
        
        public async Task ModalSubmitted(SocketModal modal)
        {
            if (modal.Data.CustomId.StartsWith("updateConfig_"))
            {
                var key = modal.Data.CustomId.Replace("updateConfig_", "");
                var iVal = modal.Data.Components.First().Value;
                PropertyInfo propertyInfo = typeof(JReader.Config).GetProperty(key);
                if (propertyInfo != null)
                {
                    var expectedType = propertyInfo.PropertyType;
                    bool parseSuc = false;
                    object? parsedVal = null;

                    if (expectedType == typeof(int))
                    {
                        parseSuc = int.TryParse(iVal, out int intRes);
                        parsedVal = intRes;
                    }
                    else if (expectedType == typeof(bool))
                    {
                        parseSuc = bool.TryParse(iVal, out bool boolRes);
                        parsedVal = boolRes;
                    }
                    else if (expectedType == typeof(ulong))
                    {
                        parseSuc = ulong.TryParse(iVal, out ulong ulongRes);
                        parsedVal = ulongRes;
                    }
                    else if (expectedType == typeof(string))
                    {
                        parsedVal = iVal;
                        parseSuc = true;
                    }
                    // TODO: dont forget to add more types when added.

                    if (parseSuc)
                    {
                        if (JReader.OverwriteConfigValue(key, parsedVal))
                        {
                            await modal.RespondAsync($"Cfg updated for `{key}` with value `{parsedVal}`.");
                        }
                        else
                        {
                            await modal.RespondAsync($"Failed to update the cfg for `{key}`.");
                        }
                    }
                    else
                    {
                        await modal.RespondAsync($"Invalid cfg value type for `{key}`, unable to parse `{iVal}` into expected `{expectedType.Name}` type.");
                    }
                }
                else
                {
                    await modal.RespondAsync($"Cfg key `{key}` does not exist in the cfg.");
                }
            }
        }




        
    }

}