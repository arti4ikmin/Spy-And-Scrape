using Discord;
using Discord.WebSocket;
using SpyAndScrape.config;

namespace SpyAndScrape;

#pragma warning disable 4014

public class NotifierBot
{
    
    
    private readonly DiscordSocketClient _client;
    private BotCmds _botCommands;
    private readonly HttpClient _httpClient;
    
    public NotifierBot()
    {
        
        _client = new DiscordSocketClient();
        _client.Log += Log;
        _client.Ready += OnReady;
        
        _httpClient = new HttpClient();
        _botCommands = new BotCmds(_client, this);
    }

    public async Task StartAsync(string botT)
    {
        await _client.LoginAsync(TokenType.Bot, botT);
        await _client.StartAsync();

        await Task.Delay(-1);
    }
    
    public async Task ShutdownBot()
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }
    private Task Log(LogMessage log)
    {
        Console.WriteLine(log);
        return Task.CompletedTask;
    }

    private async Task OnReady()
    {
        Console.WriteLine("Bot is connected");
        var args = CommandLineArgs.GetArgs();
        if (args.Length > 0 && args[0] == "delay") { SendBotMessage("Bot was successfully restarted. With delay.", 1, false); }
        else if (JReader.CurrentConfig.sendStartingMessageOnStartup == 0) { SendBotMessage("" , 0, false); }
    }
    
    // rule method (helper) to distribute the message across methods
    public async Task SendBotMessage(string msg, int logLevel = 1, bool header = true, ulong channelId = 0)
    {
        
        if (!CanSendMessages()) { Console.WriteLine("bot timed out..."); return; }
        
        if (channelId == 0)
        {
            switch (logLevel)
            {
                case 0:
                    channelId = JReader.CurrentConfig.generalBotSetupChannelId;
                    break;
                case 1:
                    channelId = JReader.CurrentConfig.generalBotLogChannelId;
                    break;
                case 2:
                    channelId = JReader.CurrentConfig.generalBotImportantChannelId;
                    break;
                case 3:
                    channelId = JReader.CurrentConfig.generalBotImportantChannelId;
                    break;
                default:
                    MethodMsgSetup("logLevel was either out of bounds (0-3)", channelId, 1);
                    break;
                
            }
        }
        switch (logLevel)
        {
            case 0:
                //setup the bot
                MethodMsgSetup(msg, channelId, 0);
                break;
            case 1:
                MethodMsgLog(msg, header, channelId);
                break;
            case 2:
                MethodMsgImportant(msg, header, channelId, false);
                break;
            case 3:
                MethodMsgImportant(msg, header, channelId, true);
                break;
            default:
                MethodMsgSetup("logLevel was either out of bounds (0-3)", channelId, 1);
                break;
            
        }

    }

    // send msg with embed (legacy)
    public async Task SendMsgEmbed(EmbedBuilder embedBuilder, ulong channelId = 0, int logLevel = 1)
    {
        if (channelId == 0) { channelId = JReader.CurrentConfig.generalBotSetupChannelId; }
        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));
        Embed embed = embedBuilder.Build();
        await channel.SendMessageAsync(embed: embed);
    }

    // send setup messge which is either 1. in the setup channel 2. starting of the bot
    public async Task MethodMsgSetup(string payload, ulong channelId, int type)
    {
        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));
        switch (type)
        {
            case 0:
                await channel.SendMessageAsync($"Hello! This is the 1st message on startup from the bot! Use /startinfo to get further information. If its the 1st startup it might take time for the slash commands to take effect.\nTrack Discord Messages: {JReader.CurrentConfig.trackDiscord}, Track Roblox: {JReader.CurrentConfig.trackRoblox}.\nTracking will begin in shortly for the activated...");
                break;
            case 1: 
                await channel.SendMessageAsync(payload);
                break;
            default:
                await channel.SendMessageAsync("Unknown call received");
                break;
            
        }
    }
    
    private async Task MethodMsgLog(string payload, bool header, ulong channelId)
    {
        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));
        
        if (channel != null)
        {
            if(header) {
                var embed = new EmbedBuilder()
                    .WithTitle("Tracker detected activity from: " + JReader.CurrentConfig.generalTargetName)
                    .WithDescription(payload)
                    .WithColor(new Discord.Color(0, 200, 255))
                    .WithFooter("Log Activity ", JReader.CurrentConfig.generalBotDecoration)
                    
                    .Build();
                await channel.SendMessageAsync(embed: embed);
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithTitle("")
                    .WithDescription(payload)
                    .WithColor(new Discord.Color(0, 200, 255))
                    .WithFooter("Log Activity ", JReader.CurrentConfig.generalBotDecoration)
                    
                    .Build();
                await channel.SendMessageAsync(embed: embed);
            }
            Console.WriteLine("lvl log msg was sent");
        }
    }
    
    private async Task MethodMsgImportant(string payload, bool header, ulong channelId, bool desktopNotify = false)
    {
        if (desktopNotify)
        {
            // TODO: COOK THIS UP
        }
        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));

        if (channel != null)
        {
            if (header) {
                var embed = new EmbedBuilder()
                    .WithTitle("Tracker detected activity from: " + JReader.CurrentConfig.generalTargetName)
                    .WithDescription(payload)
                    .WithColor(new Discord.Color(255, 0, 0))
                    .WithFooter("Elevated important Level event", JReader.CurrentConfig.generalBotDecoration)
                    .Build();

                // send outside, bc inside it wont ping
                await channel.SendMessageAsync(JReader.CurrentConfig.generalWhoToPing, embed: embed);
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithTitle("")
                    .WithDescription(payload)
                    .WithColor(new Discord.Color(255, 0, 0))
                    .WithFooter("Elevated important Level event", JReader.CurrentConfig.generalBotDecoration)
                    .Build();

                // send outside, bc inside it wont ping
                await channel.SendMessageAsync(JReader.CurrentConfig.generalWhoToPing, embed: embed);
            }
            Console.WriteLine("lvl important msg was sent");
        }
    }
    
    private DateTime? _endTime = null;

    public async Task PauseMessages(int min)
    {
        _endTime = DateTime.Now.AddMinutes(min);
        Console.WriteLine($"msgs should be paused until {_endTime}");

        await Task.Delay(TimeSpan.FromMinutes(min));

        _endTime = null;
        Console.WriteLine("msgs can now be sent again");
    }

    private bool CanSendMessages()
    {
        if (_endTime == null || DateTime.Now > _endTime) 
        { 
            return true;
        }
        return false;
    }
    

}