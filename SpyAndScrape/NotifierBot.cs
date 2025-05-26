using Discord;
using Discord.WebSocket;
using SpyAndScrape.config;

namespace SpyAndScrape;

#pragma warning disable 4014

public class NotifierBot
{
    private readonly DiscordSocketClient _client;
    private BotCmds _botCommands;
    //private readonly HttpClient _httpClient;

// --- First Run Setup State ---
    private TrackingOptionsChoice _firstRun_TrackingChoice = TrackingOptionsChoice.None;
    private List<SetupStep> _firstRun_PendingSteps = new();
    private ulong _firstRun_SetupChannelId;
    private IUserMessage _firstRun_LastPromptMessage;

    private readonly TaskCompletionSource<bool> _botReadyCompletionSource = new();

    private DateTime? _endTime;


    public NotifierBot()
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents =
                GatewayIntents.Guilds | GatewayIntents.GuildMessages |
                GatewayIntents.MessageContent
        });
        _client.Log += LogAsync;
        _client.Ready += OnClientReadyAsync;

        //_httpClient = new HttpClient();
        _botCommands = new BotCmds(_client, this);
        AssignBotCommands(_botCommands);
    }

    private void AssignBotCommands(BotCmds commands)
    {
        _botCommands = commands;
        _client.ButtonExecuted += HandleSetupButtonInteractionAsync;
        _client.ModalSubmitted += HandleSetupModalInteractionAsync;
    }


    public async Task StartAsync(string botT)
    {
        await _client.LoginAsync(TokenType.Bot, botT);
        await _client.StartAsync();
        await Task.Delay(-1); // keep alive
    }

    public async Task ShutdownBot()
    {
        await _client.StopAsync();
        await _client.LogoutAsync();
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine($"[BotLog] {log.ToString()}");
        return Task.CompletedTask;
    }

    private async Task OnClientReadyAsync()
    {
        Console.WriteLine($"[Bot] Connected as {_client.CurrentUser.Username}");
        _botReadyCompletionSource.TrySetResult(true);

        var args = CommandLineArgs.GetArgs();
        if (args.Length > 0 && args[0] == "delay")
            SendBotMessage("Bot was successfully restarted (with delay)", 1, false);
        else if (JReader.CurrentConfig.sendStartingMessageOnStartup == 0 && !JReader.IsNewCfgJustCreated)
            SendBotMessage("", 0, false);
    }

    public Task WaitForReadyAsync()
    {
        return _botReadyCompletionSource.Task;
    }


    public async Task InitiateFirstRunSetupAsync(ulong setupChannelId)
    {
        if (!JReader.IsNewCfgJustCreated) return; // guard

        _firstRun_SetupChannelId = setupChannelId;
        var channel = _client.GetChannel(setupChannelId) as ISocketMessageChannel;
        if (channel == null)
        {
            Console.WriteLine($"[FirstRunSetup] Invalid setup channel ID: {setupChannelId}. Setup aborted.");
            JReader.IsNewCfgJustCreated = false;
            return;
        }

        Console.WriteLine($"[FirstRunSetup] Initiating on channel {setupChannelId}.");

        var componentBuilder = new ComponentBuilder()
            .WithButton("Track Discord Messages", "firstrun_choice_discord")
            .WithButton("Track Roblox Activity", "firstrun_choice_roblox")
            .WithButton("Track Both", "firstrun_choice_both", ButtonStyle.Success);

        _firstRun_LastPromptMessage = await channel.SendMessageAsync(
            "**Welcome to SpyAndScrape FirstRun Setup!**\n" +
            "This bot needs some init configuration to get started\n\n" +
            "Please choose which services you'd like to track:",
            components: componentBuilder.Build());
    }

    private async Task HandleSetupButtonInteractionAsync(SocketMessageComponent component)
    {
        if (!component.Data.CustomId.StartsWith("firstrun_")) return;
        if (_firstRun_SetupChannelId == 0 || component.Channel.Id != _firstRun_SetupChannelId ||
            !JReader.IsNewCfgJustCreated)
        {
            await component.RespondAsync(
                "This button is part of the FirstRun setup process and cant be used outside of it or in this channel",
                ephemeral: true);
            return;
        }


        var choice = component.Data.CustomId;
        var handled = false;

        if (choice.StartsWith("firstrun_choice_"))
        {
            handled = true;
            switch (choice)
            {
                case "firstrun_choice_discord": _firstRun_TrackingChoice = TrackingOptionsChoice.Discord; break;
                case "firstrun_choice_roblox": _firstRun_TrackingChoice = TrackingOptionsChoice.Roblox; break;
                case "firstrun_choice_both": _firstRun_TrackingChoice = TrackingOptionsChoice.Both; break;
            }

            if (handled)
            {
                await component.UpdateAsync(p =>
                {
                    p.Content = $"Great! You've chosen to track: **{_firstRun_TrackingChoice}**. Let's configure that.";
                    p.Components = null; // buttons removal
                });
                PopulateSetupSteps();
                await ProcessNextSetupStepAsync(component);
            }
        }
        else if (choice.StartsWith("firstrun_setval_"))
        {
            handled = true;
            var configKey = choice.Replace("firstrun_setval_", "");
            var step = _firstRun_PendingSteps.FirstOrDefault(s => s.ConfigKey == configKey);
            if (step != null)
            {
                var modal = new ModalBuilder()
                    .WithTitle(step.ModalTitle)
                    .WithCustomId($"firstrun_modal_{configKey}")
                    .AddTextInput(step.ModalInputLabel, "input_value", step.ModalInputStyle, step.ModalPlaceholder);
                await component.RespondWithModalAsync(modal.Build());
            }
            else
            {
                await component.RespondAsync("Err: Setup step not found.", ephemeral: true);
            }
        }
        else if (choice.StartsWith("firstrun_setdefault_"))
        {
            handled = true;
            var configKey = choice.Replace("firstrun_setdefault_", "");
            var step = _firstRun_PendingSteps.FirstOrDefault(s => s.ConfigKey == configKey);

            if (step == null)
            {
                await component.RespondAsync("Err: Setup step for default not found", ephemeral: true);
                return;
            }

            object? defaultValueToSet;
            var defaultConfig = new JReader.Config(); // Fresh instance for true defaults

            switch (configKey)
            {
                case "generalBotLogChannelId":
                case "generalBotImportantChannelId":
                    if (JReader.CurrentConfig.generalBotSetupChannelId != 0)
                    {
                        defaultValueToSet = JReader.CurrentConfig.generalBotSetupChannelId;
                    }
                    else // Fallback if setup channel ID isn't somehow set (should be by this point)
                    {
                        var prop = typeof(JReader.Config).GetProperty(configKey);
                        defaultValueToSet = prop?.GetValue(defaultConfig); // Default to its own default
                    }

                    break;
                default:
                    var property = typeof(JReader.Config).GetProperty(configKey);
                    if (property == null)
                    {
                        await component.RespondAsync($"Err: Cfg key '{configKey}' not found for default",
                            ephemeral: true);
                        return;
                    }

                    defaultValueToSet = property.GetValue(defaultConfig);
                    break;
            }

            if (defaultValueToSet == null && configKey == "generalBotDecoration")
            {
                defaultValueToSet = "";
            }
            else if (defaultValueToSet == null)
            {
                await component.RespondAsync($"Err: Could not determine default value for '{configKey}'.",
                    ephemeral: true);
                return;
            }


            var success = JReader.OverwriteConfigValue(configKey, defaultValueToSet);

            if (success)
            {
                _firstRun_PendingSteps.Remove(step);
                await component.RespondAsync(
                    $"Default value for '{step.ModalTitle}' has been set to `{defaultValueToSet}`", ephemeral: true);
                await ProcessNextSetupStepAsync(component);
            }
            else
            {
                await component.RespondAsync($"Failed to set default for {step.ModalTitle}. Pls check logs (/debug)",
                    ephemeral: true);
            }
        }

        if (!handled && !component.HasResponded) await component.RespondAsync("Unknown setup action", ephemeral: true);
    }

    private void PopulateSetupSteps()
    {
        _firstRun_PendingSteps.Clear();


        if (_firstRun_TrackingChoice == TrackingOptionsChoice.Discord ||
            _firstRun_TrackingChoice == TrackingOptionsChoice.Both)
        {
            _firstRun_PendingSteps.Add(new SetupStep
            {
                ConfigKey = "discordTrackingUsrId",
                PromptMessage = "Provide the **Discord Id** for message tracking:",
                ButtonText = "Set Discord Id",
                ModalTitle = "Discord Id",
                ModalInputLabel = "Id"
            });
            _firstRun_PendingSteps.Add(new SetupStep
            {
                ConfigKey = "discordTrackingToken",
                PromptMessage = "Provide the **Discord User Token** for message tracking.\n*(This is a sensitive value. The bot will only use it to listen for messages from the specified user.)*",
                ButtonText = "Set Discord Token",
                ModalTitle = "Discord User Token",
                ModalInputLabel = "Token",
                ModalInputStyle = TextInputStyle.Paragraph,
                ModalPlaceholder = "Enter the user token"
            });
        }

        if (_firstRun_TrackingChoice == TrackingOptionsChoice.Roblox || _firstRun_TrackingChoice == TrackingOptionsChoice.Both)
            _firstRun_PendingSteps.Add(new SetupStep
            {
                ConfigKey = "robloxTrackingUserId",
                PromptMessage = "Provide the **Roblox User ID** for activity tracking:",
                ButtonText = "Set Roblox User ID",
                ModalTitle = "Roblox User ID",
                ModalInputLabel = "User ID",
                Validator = (input) =>
                {
                    var isValid = ulong.TryParse(input, out var val) && val != 0;
                    return (isValid, isValid ? null : "Invalid User ID. Cant be a zero", isValid ? val : null);
                }
            });

        _firstRun_PendingSteps.Add(new SetupStep
        {
            ConfigKey = "generalBotSetupChannelId",
            PromptMessage = "Provide the **Channel ID** where setup messages (like this one) will be sent:",
            ButtonText = "Set Setup Channel ID",
            ModalTitle = "Setup Channel ID",
            ModalInputLabel = "Channel ID (numbers only)",
            Validator = (input) =>
            {
                var isValid = ulong.TryParse(input, out var val) && val != 0;
                return (isValid, isValid ? null : "Invalid Channel ID. Cant be a zero", isValid ? val : null);
            }
        });
        _firstRun_PendingSteps.Add(new SetupStep
        {
            ConfigKey = "generalBotLogChannelId",
            PromptMessage = "Please provide the **Channel ID** for regular bot activity logs:",
            ButtonText = "Set Log Channel ID",
            ModalTitle = "Log Channel ID",
            ModalInputLabel = "Channel ID (numbers only)",
            Validator = (input) =>
            {
                var isValid = ulong.TryParse(input, out var val) && val != 0;
                return (isValid, isValid ? null : "Invalid Channel ID. Cant be a zero", isValid ? val : null);
            }
        });
        _firstRun_PendingSteps.Add(new SetupStep
        {
            ConfigKey = "generalBotImportantChannelId",
            PromptMessage = "Please provide the **Channel ID** for important notifications (mentions included):",
            ButtonText = "Set Important Channel ID",
            ModalTitle = "Important Channel ID",
            ModalInputLabel = "Channel ID (numbers only)",
            Validator = (input) =>
            {
                var isValid = ulong.TryParse(input, out var val) && val != 0;
                return (isValid, isValid ? null : "Invalid Channel ID. Cant be a zero", isValid ? val : null);
            }
        });
        _firstRun_PendingSteps.Add(new SetupStep
        {
            ConfigKey = "generalWhoToPing",
            PromptMessage = "Please provide the **User ID, Role ID, or general ping** (`@here`, `@everyone`) for important notifications:",
            ButtonText = "Set Ping Target",
            ModalTitle = "Ping Target",
            ModalInputLabel = "ID or Ping (e.g., <@123456789012345678> or @here)"
        });
        _firstRun_PendingSteps.Add(new SetupStep
        {
            ConfigKey = "generalTargetName",
            PromptMessage = "Please provide the **Name** of the target being tracked (visual only; what the bot sends):",
            ButtonText = "Set Target Name",
            ModalTitle = "Target Name",
            ModalInputLabel = "Name"
        });
        // _firstRun_PendingSteps.Add(new SetupStep
        // {
        //     ConfigKey = "generalBotDecoration",
        //     PromptMessage = "Optional: Provide a **URL for a small image** to use in the footer of embeds (visual only):",
        //     ButtonText = "Set Decoration Image URL",
        //     ModalTitle = "Decoration Image URL",
        //     ModalInputLabel = "Image URL (optional)",
        //     ModalPlaceholder = "Leave blank or enter URL",
        //     Validator = (input) =>
        //     {
        //         if (string.IsNullOrWhiteSpace(input)) return (true, null, input);
        //         var isValid = Uri.TryCreate(input, UriKind.Absolute, out var uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        //         return (isValid, isValid ? null : "Invalid URL format", input);
        //     }
        // });
    }

    private async Task ProcessNextSetupStepAsync(SocketInteraction followupInteraction = null)
    {
        if (_firstRun_SetupChannelId == 0 || !JReader.IsNewCfgJustCreated) return;

        var channel = _client.GetChannel(_firstRun_SetupChannelId) as ISocketMessageChannel;
        if (channel == null)
        {
            Console.WriteLine("[FirstRunSetup] Channel now null during step processing (tf??? Id assume its deleted but whatever)");
            JReader.IsNewCfgJustCreated = false;
            _firstRun_SetupChannelId = 0;
            return;
        }

        if (!_firstRun_PendingSteps.Any())
        {
            var completionMessage = "**FirstRun Setup Complete!**\n";
            if (_firstRun_TrackingChoice != TrackingOptionsChoice.None)
            {
                completionMessage += $"Tracking for **{_firstRun_TrackingChoice}** is now configured with the essential settings.\n";
                if (_firstRun_TrackingChoice == TrackingOptionsChoice.Discord || _firstRun_TrackingChoice == TrackingOptionsChoice.Both)
                {
                    JReader.OverwriteConfigValue("trackDiscord", true);
                }
                if (_firstRun_TrackingChoice == TrackingOptionsChoice.Roblox || _firstRun_TrackingChoice == TrackingOptionsChoice.Both)
                {
                    JReader.OverwriteConfigValue("trackRoblox", true);
                }

                await JReader.GetStartingJsonAsync();
                completionMessage += $"Monitoring is active for Discord: `{JReader.CurrentConfig.trackDiscord}` and Roblox: `{JReader.CurrentConfig.trackRoblox}`.\n";

                JReader.IsNewCfgJustCreated = false;
            }
            else
            {
                completionMessage += "No tracking options were selected for setup. You can enable tracking later via cmds(/configchange) or by editing `config.json`.\n";
            }

            completionMessage += "You can adjust these and other settings using the `/configchange` cmd or by directly editing `config.json` and restarting the bot\n\n";
            completionMessage += "The bot is now fully operational";


            if (followupInteraction != null && followupInteraction.HasResponded && !(followupInteraction is SocketModal))
            {
                await followupInteraction.FollowupAsync(completionMessage, ephemeral: false);
            }
            else if (_firstRun_LastPromptMessage != null)
            {
                await _firstRun_LastPromptMessage.ModifyAsync(m =>
                {
                    m.Content = completionMessage;
                    m.Components = null;
                });
            }
            else
            {
                await channel.SendMessageAsync(completionMessage);
            }
            JReader.IsNewCfgJustCreated = false; // globally
            Console.WriteLine("[FirstRunSetup] setup done");
            _firstRun_SetupChannelId = 0;
            return;
        }

        var step = _firstRun_PendingSteps.First();
        var cb = new ComponentBuilder().WithButton(step.ButtonText, $"firstrun_setval_{step.ConfigKey}", ButtonStyle.Success);

        var stepsWithDefaultButton = new List<string>
        {
            "generalBotSetupChannelId",
            "generalBotLogChannelId",
            "generalBotImportantChannelId",
            "generalWhoToPing",
            "generalTargetName"
            // "generalBotDecoration"
        };
        if (stepsWithDefaultButton.Contains(step.ConfigKey))
            cb.WithButton($"Use Default ({GetDefaultValuePreview(step.ConfigKey)})",
                $"firstrun_setdefault_{step.ConfigKey}", ButtonStyle.Secondary, row: 1); // Add to a new row

        // is last msg exists and belongs to bot, otherwise send a new one
        var canModifyLastMessage = _firstRun_LastPromptMessage != null && _firstRun_LastPromptMessage.Author.Id == _client.CurrentUser.Id && _firstRun_LastPromptMessage.Channel.Id == _firstRun_SetupChannelId;


        if (followupInteraction != null && followupInteraction.HasResponded && !(followupInteraction is SocketModal))
        {
            // if we are following up a button click that has already been responded to (updated or deferred)
            _firstRun_LastPromptMessage = await followupInteraction.FollowupAsync(step.PromptMessage, components: cb.Build(), ephemeral: false);
        }
        else if (canModifyLastMessage) await _firstRun_LastPromptMessage.ModifyAsync(m =>
        {
            m.Content = step.PromptMessage;
            m.Components = cb.Build();
        });
        else
        {
            _firstRun_LastPromptMessage = await channel.SendMessageAsync(step.PromptMessage, components: cb.Build());
        }
    }

    private string GetDefaultValuePreview(string configKey)
    {
        var defaultConfig = new JReader.Config();
        object? defaultValueObj = null;

        switch (configKey)
        {
            case "generalBotLogChannelId":
            case "generalBotImportantChannelId":
                defaultValueObj = JReader.CurrentConfig.generalBotSetupChannelId != 0 ? JReader.CurrentConfig.generalBotSetupChannelId.ToString() : "Use Setup Channel";
                break;
            default:
                var property = typeof(JReader.Config).GetProperty(configKey);
                if (property != null) defaultValueObj = property.GetValue(defaultConfig);
                break;
        }

        var preview = defaultValueObj?.ToString() ?? "N/A";
        if (preview.Length > 20)
        {
            preview = preview.Substring(0, 17) + "...";
        }
        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = "[empty]";
        }
        return preview;
    }

    private async Task HandleSetupModalInteractionAsync(SocketModal modal)
    {
        if (!modal.Data.CustomId.StartsWith("firstrun_modal_")) return;
        // if the interaction is from the designated setup channel + setup is active
        if (_firstRun_SetupChannelId == 0 || modal.Channel.Id != _firstRun_SetupChannelId || !JReader.IsNewCfgJustCreated)
        {
            await modal.RespondAsync(
                "This modal is part of the FirstRun setup process and cant be used outside of it or in this channel",
                ephemeral: true);
            return;
        }

        var configKey = modal.Data.CustomId.Replace("firstrun_modal_", "");
        var inputValue = modal.Data.Components.FirstOrDefault(c => c.CustomId == "input_value")?.Value;
        if (inputValue == null)
        {
            await modal.RespondAsync("Err: Modal input value not found", ephemeral: true);
            return;
        }


        var step = _firstRun_PendingSteps.FirstOrDefault(s => s.ConfigKey == configKey);
        if (step == null)
        {
            await modal.RespondAsync("Err: Original setup step not found in the pending list", ephemeral: true);
            return;
        }

        object parsedValue = inputValue;

        var validationResult = step.Validator(inputValue);
        if (!validationResult.isValid)
        {
            await modal.RespondAsync(
                $"Invalid input for '{step.ModalTitle}': {validationResult.errorMessage}\nPlease click '{step.ButtonText}' again to retry",
                ephemeral: true);
            return;
        }

        parsedValue = validationResult.parsedValue;

        var success = JReader.OverwriteConfigValue(configKey, parsedValue);
        if (success)
        {
            _firstRun_PendingSteps.Remove(step);
            await modal.RespondAsync($"{step.ModalTitle} has been set!", ephemeral: true);
            await ProcessNextSetupStepAsync(modal);
        }
        else
        {
            await modal.RespondAsync($"Failed to set {step.ModalTitle} Please check logs (/debug) and try again via the button", ephemeral: true);
        }
    }


    // rule method (helper) to distribute the message across methods
    public async Task SendBotMessage(string msg, int logLevel = 1, bool header = true, ulong channelId = 0)
    {
        if (_client.ConnectionState != ConnectionState.Connected)
        {
            Console.WriteLine("[SendBotMessage] Bot not connected msg not sent");
            return;
        }


        var effectiveChannelId = channelId == 0 ? DetermineChannelFromLogLevel(logLevel) : channelId;

        if (JReader.IsNewCfgJustCreated && _firstRun_SetupChannelId != 0 && effectiveChannelId == _firstRun_SetupChannelId)
            Console.WriteLine($"[SendBotMessage] First run setup active on channel {_firstRun_SetupChannelId}");
        // decide whether to return or still attempt sending (risks cluttering setup)
        // return;
        // For now, we'll let it potentially send, but maybe log the deferral
        // setup msgs are sent using different methods tied to the interaction context anyway
        if (!CanSendMessages())
        {
            Console.WriteLine("bot timed out (paused)... message not sent.");
            return;
        }


        // if channelId was 0, it's now determined by logLevel via effectiveChannelId
        if (channelId == 0)
        {
            channelId = effectiveChannelId;
            if (channelId == 0)
            {
                MethodMsgSetup($"Could not determine channel for logLevel {logLevel}. Config value might be missing or 0.", _firstRun_SetupChannelId != 0 ? _firstRun_SetupChannelId : 0, 1);
                Console.WriteLine($"[SendBotMessage] Failed to determine channel for logLevel {logLevel}. Cfg values might be missing.");
                return;
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
                MethodMsgSetup($"logLevel was either out of bounds (0-3). Received: {logLevel}", channelId, 1);
                break;
        }
    }

    private ulong DetermineChannelFromLogLevel(int logLevel)
    {
        switch (logLevel)
        {
            case 0: return JReader.CurrentConfig.generalBotSetupChannelId;
            case 1: return JReader.CurrentConfig.generalBotLogChannelId;
            case 2:
            case 3: return JReader.CurrentConfig.generalBotImportantChannelId;
            default: return 0;
        }
    }


    // send msg with embed (legacy)
    public async Task SendMsgEmbed(EmbedBuilder embedBuilder, ulong channelId = 0, int logLevel = 1)
    {
        if (_client.ConnectionState != ConnectionState.Connected)
        {
            Console.WriteLine("[SendMsgEmbed] Bot not connected. Message not sent.");
            return;
        }

        var effectiveChannelId = channelId == 0 ? DetermineChannelFromLogLevel(logLevel) : channelId;

        if (effectiveChannelId == 0)
        {
            Console.WriteLine($"[SendMsgEmbed] Failed to determine channel for logLevel {logLevel}. Cfg values might be missing.");
            return;
        }

        if (JReader.IsNewCfgJustCreated && _firstRun_SetupChannelId != 0 &&
            effectiveChannelId == _firstRun_SetupChannelId)
            Console.WriteLine($"[SendMsgEmbed] First run setup active on channel {_firstRun_SetupChannelId}. Deferring standard embed to setup channel");


        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(effectiveChannelId));

        if (channel != null)
        {
            var embed = embedBuilder.Build();
            await channel.SendMessageAsync(embed: embed);
        }
        else
        {
            Console.WriteLine($"[SendMsgEmbed] channel {effectiveChannelId} not found or is not a text channel");
        }
    }

    // send setup message which is either 1. in the setup channel 2. starting of the bot
    public async Task MethodMsgSetup(string payload, ulong channelId, int type)
    {
        if (_client.ConnectionState != ConnectionState.Connected) return;

        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));

        if (channel != null)
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
        else
            Console.WriteLine($"[MethodMsgSetup] Target channel {channelId} not found or is not a text channel");
    }

    private async Task MethodMsgLog(string payload, bool header, ulong channelId)
    {
        if (_client.ConnectionState != ConnectionState.Connected) return;

        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));

        if (channel != null)
        {
            var embed = new EmbedBuilder()
                .WithDescription(payload)
                .WithColor(new Discord.Color(0, 200, 255))
                .WithFooter("Log Activity ", JReader.CurrentConfig.generalBotDecoration);

            if (header)
                embed.WithTitle("Tracker detected activity from: " + JReader.CurrentConfig.generalTargetName);
            else
                embed.WithTitle("");


            await channel.SendMessageAsync(embed: embed.Build());
            Console.WriteLine("lvl log msg was sent");
        }
        else
        {
            Console.WriteLine($"[MethodMsgLog] Target channel {channelId} not found or is not a text channel.");
        }
    }

    private async Task MethodMsgImportant(string payload, bool header, ulong channelId, bool desktopNotify = false)
    {
        if (_client.ConnectionState != ConnectionState.Connected) return;

        if (desktopNotify)
        {
            // TODO: COOK THIS UP
        }

        var channel = (ITextChannel)await _client.GetChannelAsync(Convert.ToUInt64(channelId));

        if (channel != null)
        {
            var embed = new EmbedBuilder()
                .WithDescription(payload)
                .WithColor(new Discord.Color(255, 0, 0))
                .WithFooter("Elevated important Level event",
                    JReader.CurrentConfig.generalBotDecoration);

            if (header)
                embed.WithTitle("Tracker detected activity from: " + JReader.CurrentConfig.generalTargetName);
            else
                embed.WithTitle("");

            var pingTarget = JReader.CurrentConfig.generalWhoToPing;
            if (string.IsNullOrWhiteSpace(pingTarget))
            {
                Console.WriteLine("[MethodMsgImportant] generalWhoToPing is not set Sending important message without ping");
                await channel.SendMessageAsync(embed: embed.Build());
            }
            else
            {
                await channel.SendMessageAsync(pingTarget, embed: embed.Build());
            }


            Console.WriteLine("[MethodMsgImportant] lvl important msg was sent");
        }
        else
        {
            Console.WriteLine($"[MethodMsgImportant] Target channel {channelId} not found or is not a text channel");
        }
    }

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
        if (_endTime == null || DateTime.Now > _endTime) return true;
        return false;
    }
    
    public ulong GetGuildId(ulong channelId)
    {
        if (channelId == 0) return 0;

        var channel = _client.GetChannel(channelId) as SocketGuildChannel;
        return channel?.Guild.Id ?? 0;
    }
    
}

public enum TrackingOptionsChoice
{
    None,
    Discord,
    Roblox,
    Both
}

public class SetupStep
{
    public string ConfigKey { get; set; }
    public string PromptMessage { get; set; }
    public string ButtonText { get; set; }
    public string ModalTitle { get; set; }
    public string ModalInputLabel { get; set; }
    public TextInputStyle ModalInputStyle { get; set; } = TextInputStyle.Short;
    public string ModalPlaceholder { get; set; } = "Enter value here";
    public Func<string, (bool isValid, string errorMessage, object parsedValue)> Validator { get; set; }
}