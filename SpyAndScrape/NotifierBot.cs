using Discord;
using Discord.WebSocket;
using SpyAndScrape.config;

namespace SpyAndScrape;

#pragma warning disable 4014

public class NotifierBot
{
    private readonly DiscordSocketClient _client;
    private BotCmds _botCommands;

    private List<string> _activeSetup_ServicesBeingConfigured = new();
    private List<SetupStep> _activeSetup_PendingSteps = new();
    private ulong _activeSetup_ChannelId;
    private IUserMessage? _activeSetup_LastPromptMessage;
    private SocketInteraction? _activeSetup_LastInteraction;
    private bool _isInteractiveSetupActive;


    private List<TrackableService> _availableServices;


    private readonly TaskCompletionSource<bool> _botReadyCompletionSource = new();

    private DateTime? _endTime;

    public DiscordSocketClient Client => _client;
    
    public NotifierBot()
    {
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent | GatewayIntents.GuildMessageReactions
        });
        _client.Log += LogAsync;
        _client.Ready += OnClientReadyAsync;
        //_httpClient = new HttpClient();

        InitializeAvailableServices();
    }

    private void InitializeAvailableServices()
    {
        _availableServices = new List<TrackableService>
        {
            new()
            {
                Id = "core_settings",
                Name = "Core Bot Settings",
                EnableConfigKey = null,
                IsCoreSetting = true,
                SetupSteps = new List<SetupStep>
                {
                    new()
                    {
                        ConfigKey = "generalBotSetupChannelId",
                        PromptMessage = "Provide the **Channel ID** where setup messages (like this one) will be sent:",
                        ButtonText = "Set Setup Channel ID",
                        ModalTitle = "Setup Channel ID",
                        ModalInputLabel = "Channel ID (numbers only)",
                        Validator = (input) =>
                        {
                            var isValid = ulong.TryParse(input, out var val) && val != 0;
                            return (isValid, isValid ? null : "Invalid Channel ID. Cant be a zero", val);
                        }
                    },
                    new()
                    {
                        ConfigKey = "generalBotLogChannelId",
                        PromptMessage = "Please provide the **Channel ID** for regular bot activity logs:",
                        ButtonText = "Set Log Channel ID",
                        ModalTitle = "Log Channel ID",
                        ModalInputLabel = "Channel ID (numbers only)",
                        Validator = (input) =>
                        {
                            var isValid = ulong.TryParse(input, out var val) && val != 0;
                            return (isValid, isValid ? null : "Invalid Channel ID. Cant be a zero", val);
                        }
                    },
                    new()
                    {
                        ConfigKey = "generalBotImportantChannelId",
                        PromptMessage =
                            "Please provide the **Channel ID** for important notifications (mentions included):",
                        ButtonText = "Set Important Channel ID",
                        ModalTitle = "Important Channel ID",
                        ModalInputLabel = "Channel ID (numbers only)",
                        Validator = (input) =>
                        {
                            var isValid = ulong.TryParse(input, out var val) && val != 0;
                            return (isValid, isValid ? null : "Invalid Channel ID. Cant be a zero", val);
                        }
                    },
                    new()
                    {
                        ConfigKey = "generalWhoToPing",
                        PromptMessage =
                            "Please provide the **User ID, Role ID, or general ping** (`@here`, `@everyone`) for important notifications:",
                        ButtonText = "Set Ping Target",
                        ModalTitle = "Ping Target",
                        ModalInputLabel = "ID or Ping (e.g., <@123456789012345678> or @here)"
                    },
                    new()
                    {
                        ConfigKey = "generalTargetName",
                        PromptMessage =
                            "Please provide the **Name** of the target being tracked (visual only; what the bot sends):",
                        ButtonText = "Set Target Name",
                        ModalTitle = "Target Name",
                        ModalInputLabel = "Name"
                    }
                    // new SetupStep
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
                    // }
                }
            },
            new()
            {
                Id = "discord_tracking",
                Name = "Discord Activity Tracking",
                EnableConfigKey = "trackDiscord",
                SetupSteps = new List<SetupStep>
                {
                    new()
                    {
                        ConfigKey = "discordTrackingUsrId",
                        PromptMessage = "Provide the **Discord User ID** for message and profile tracking:",
                        ButtonText = "Set Discord User ID",
                        ModalTitle = "Discord User ID",
                        ModalInputLabel = "User ID (numbers only)",
                        Validator = (input) =>
                        {
                            var isValid = ulong.TryParse(input, out var val) && val != 0;
                            return (isValid, isValid ? null : "Invalid User ID. Cant be a zero", val);
                        }
                    },
                    new()
                    {
                        ConfigKey = "discordTrackingToken",
                        PromptMessage =
                            "Provide the **Discord User Token** for message and profile tracking.\n*(This is a sensitive value. The bot will only use it to listen for messages and fetch profile data from the specified user.)*",
                        ButtonText = "Set Discord Token",
                        ModalTitle = "Discord User Token",
                        ModalInputLabel = "Token",
                        ModalInputStyle = TextInputStyle.Paragraph,
                        ModalPlaceholder = "Enter the user token"
                    }
                }
            },
            new()
            {
                Id = "roblox_tracking",
                Name = "Roblox Activity Tracking",
                EnableConfigKey = "trackRoblox",
                SetupSteps = new List<SetupStep>
                {
                    new()
                    {
                        ConfigKey = "robloxTrackingUserId",
                        PromptMessage = "Provide the **Roblox User ID** for activity tracking:",
                        ButtonText = "Set Roblox User ID",
                        ModalTitle = "Roblox User ID",
                        ModalInputLabel = "User ID",
                        Validator = (input) =>
                        {
                            var isValid = ulong.TryParse(input, out var val) && val != 0;
                            return (isValid, isValid ? null : "Invalid User ID. Cant be a zero", val);
                        }
                    }
                }
            }
        };
    }


    public void AssignBotCommands(BotCmds commands)
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

    public bool IsInteractiveSetupActive()
    {
        return _isInteractiveSetupActive;
    }


    public async Task InitiateFirstRunSetupAsync(ulong setupChannelId)
    {
        if (!JReader.IsNewCfgJustCreated) return;

        _isInteractiveSetupActive = true;
        _activeSetup_ChannelId = setupChannelId;
        _activeSetup_ServicesBeingConfigured.Clear();
        _activeSetup_PendingSteps.Clear();
        _activeSetup_LastInteraction = null;


        var channel = _client.GetChannel(setupChannelId) as ISocketMessageChannel;
        if (channel == null)
        {
            Console.WriteLine($"[FirstRunSetup] Invalid setup channel ID: {setupChannelId}. Setup aborted.");
            JReader.IsNewCfgJustCreated = false; // normal opr if channel is bad
            _isInteractiveSetupActive = false;
            return;
        }

        Console.WriteLine($"[FirstRunSetup] Initiating on channel {setupChannelId}.");

        var menuBuilder = new SelectMenuBuilder()
            .WithPlaceholder("Select services to set up")
            .WithCustomId("setup_service_selection_firstrun")
            .WithMinValues(1)
            .WithMaxValues(_availableServices.Count(s =>
                !s.IsCoreSetting));

        foreach (var service in
                 _availableServices.Where(s => !s.IsCoreSetting)) // Don't let user select core, it's implied
            menuBuilder.AddOption(service.Name, service.Id, $"Configure {service.Name}");

        var componentBuilder = new ComponentBuilder().WithSelectMenu(menuBuilder);

        _activeSetup_LastPromptMessage = await channel.SendMessageAsync(
            "**Welcome to SpyAndScrape FirstRun Setup!**\n" +
            "This bot needs some init configuration to get started\n\n" +
            "Please choose which services you'd like to track:",
            components: componentBuilder.Build());
    }

    public async Task InitiateReconfigurationAsync(SocketSlashCommand cmd)
    {
        if (_isInteractiveSetupActive)
        {
            await cmd.RespondAsync("Another setup process is already active. Please complete or cancel it first.",
                ephemeral: true);
            return;
        }

        _isInteractiveSetupActive = true;
        _activeSetup_ChannelId = cmd.Channel.Id;
        _activeSetup_ServicesBeingConfigured.Clear();
        _activeSetup_PendingSteps.Clear();
        _activeSetup_LastInteraction = cmd;


        var menuBuilder = new SelectMenuBuilder()
            .WithPlaceholder("Select features to reconfigure")
            .WithCustomId("setup_service_selection_reconfig")
            .WithMinValues(1)
            .WithMaxValues(_availableServices.Count);

        foreach (var service in _availableServices)
            menuBuilder.AddOption(service.Name, service.Id, $"Configure {service.Name}");

        var componentBuilder = new ComponentBuilder().WithSelectMenu(menuBuilder);


        await cmd.RespondAsync(
            "**Bot Reconfiguration**\n" +
            "Select the features or settings you want to reconfigure:",
            components: componentBuilder.Build(),
            ephemeral: true // MAYBE
        );
        _activeSetup_LastPromptMessage = null;
    }


    public async Task HandleSetupServiceSelectionMenuAsync(SocketMessageComponent component)
    {
        if (!component.Data.CustomId.StartsWith("setup_service_selection_")) return;

        if (!_isInteractiveSetupActive || component.Channel.Id != _activeSetup_ChannelId)
        {
            await component.RespondAsync("This selection is part of an active setup process and cant be used outside of it, in this channel, or the process has expired",
                ephemeral: true);
            return;
        }

        _activeSetup_LastInteraction = component;

        _activeSetup_ServicesBeingConfigured = component.Data.Values.ToList();

        var selectedServiceNames = _activeSetup_ServicesBeingConfigured
            .Select(id => _availableServices.FirstOrDefault(s => s.Id == id)?.Name)
            .Where(name => name != null)
            .ToList();

        var updateMessage =
            $"Chosen to configure: **{string.Join(", ", selectedServiceNames)}**, proceed";

        if (component.Data.CustomId == "setup_service_selection_firstrun" && _activeSetup_LastPromptMessage != null)
            await component.UpdateAsync(p =>
            {
                p.Content = updateMessage;
                p.Components = null;
            });
        else 
            await component.RespondAsync(updateMessage, ephemeral: true);
        PopulateSetupStepsForActiveConfiguration();
        await ProcessNextSetupStepAsync();
    }


    private async Task HandleSetupButtonInteractionAsync(SocketMessageComponent component)
    {
        if (!component.Data.CustomId.StartsWith("setup_")) return;
        if (!_isInteractiveSetupActive || component.Channel.Id != _activeSetup_ChannelId)
        {
            await component.RespondAsync(
                "This button is part of the FirstRun setup process and cant be used outside of it or in this channel",
                ephemeral: true);
            return;
        }

        _activeSetup_LastInteraction = component;


        var choice = component.Data.CustomId;
        var handled = false;

        if (choice.StartsWith("setup_setval_"))
        {
            handled = true;
            var configKey = choice.Replace("setup_setval_", "");
            var step = _activeSetup_PendingSteps.FirstOrDefault(s => s.ConfigKey == configKey);
            if (step != null)
            {
                var modal = new ModalBuilder()
                    .WithTitle(step.ModalTitle)
                    .WithCustomId($"setup_modal_{configKey}")
                    .AddTextInput(step.ModalInputLabel, "input_value", step.ModalInputStyle, step.ModalPlaceholder);
                await component.RespondWithModalAsync(modal.Build());
            }
            else
            {
                await component.RespondAsync("Err: Setup step not found.", ephemeral: true);
            }
        }
        else if (choice.StartsWith("setup_setdefault_"))
        {
            handled = true;
            var configKey = choice.Replace("setup_setdefault_", "");
            var step = _activeSetup_PendingSteps.FirstOrDefault(s => s.ConfigKey == configKey);

            if (step == null)
            {
                await component.RespondAsync("Err: Setup step for default not found", ephemeral: true);
                return;
            }

            object? defaultValueToSet;
            var defaultConfig = new JReader.Config();

            switch (configKey)
            {
                case "generalBotLogChannelId":
                    
                case "generalBotImportantChannelId":
                    if (JReader.CurrentConfig.generalBotSetupChannelId != 0)
                    {
                        defaultValueToSet = JReader.CurrentConfig.generalBotSetupChannelId;
                    }
                    else
                    {
                        var prop = typeof(JReader.Config).GetProperty(configKey);
                        defaultValueToSet = prop?.GetValue(defaultConfig);
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
                _activeSetup_PendingSteps.Remove(step);
                // Ephemeral response to the button click
                await component.RespondAsync(
                    $"Default value for '{step.ModalTitle}' has been set to `{defaultValueToSet}`.", ephemeral: true);
                await ProcessNextSetupStepAsync();
            }
            else
            {
                await component.RespondAsync($"Failed to set default for {step.ModalTitle}. Pls check logs (/debug)",
                    ephemeral: true);
            }
        }

        if (!handled && !component.HasResponded) await component.RespondAsync("Unknown setup action", ephemeral: true);
    }

    private void PopulateSetupStepsForActiveConfiguration()
    {
        _activeSetup_PendingSteps.Clear();
        if (JReader.IsNewCfgJustCreated || _activeSetup_ServicesBeingConfigured.Contains("core_settings"))
        {
            var coreService = _availableServices.FirstOrDefault(s => s.Id == "core_settings");
            if (coreService != null) _activeSetup_PendingSteps.AddRange(coreService.SetupSteps);
        }

        foreach (var serviceId in _activeSetup_ServicesBeingConfigured)
        {
            var service = _availableServices.FirstOrDefault(s => s.Id == serviceId);
            if (service != null && service.Id != "core_settings")
                _activeSetup_PendingSteps.AddRange(service.SetupSteps);
        }


        
        
        
        
        
        _activeSetup_PendingSteps = _activeSetup_PendingSteps.DistinctBy(s => s.ConfigKey).ToList();
    }


    private async Task ProcessNextSetupStepAsync()
    {
        if (!_isInteractiveSetupActive) return;

        var channel = _client.GetChannel(_activeSetup_ChannelId) as ISocketMessageChannel;
        if (channel == null)
        {
            Console.WriteLine("[InteractiveSetup] Channel now null during step processing (probably deleted)");
            ResetInteractiveSetupState();
            return;
        }

        var isEphemeralContext = _activeSetup_LastInteraction is SocketSlashCommand || (_activeSetup_LastInteraction is SocketMessageComponent msgComp);
        if (_activeSetup_LastInteraction is SocketModal)
            isEphemeralContext = true;


        if (!_activeSetup_PendingSteps.Any())
        {
            var completionMessage = "";
            if (JReader.IsNewCfgJustCreated)
            {
                completionMessage = "**FirstRun Setup Complete!**\n";
                var configuredServiceNames = new List<string>();

                foreach (var serviceId in _activeSetup_ServicesBeingConfigured)
                {
                    var service = _availableServices.FirstOrDefault(s => s.Id == serviceId);
                    if (service != null)
                    {
                        configuredServiceNames.Add(service.Name);
                        if (!string.IsNullOrEmpty(service.EnableConfigKey))
                            JReader.OverwriteConfigValue(service.EnableConfigKey, true);
                    }
                }

                await JReader.GetStartingJsonAsync();

                if (configuredServiceNames.Any())
                    completionMessage += $"Configured: **{string.Join(", ", configuredServiceNames)}**.\n";
                completionMessage += $"Core settings have also been configured\n";
                completionMessage +=
                    $"Monitoring is active for Discord: `{JReader.CurrentConfig.trackDiscord}` and Roblox: `{JReader.CurrentConfig.trackRoblox}` (and any other enabled services).\n";
                completionMessage +=
                    "You can adjust these and other settings using `/configchange` or `/reconfigure`.\n\n";
                completionMessage += "The bot is now fully operational.";

                JReader.IsNewCfgJustCreated = false;
                Program.StartTrackersAfterSetup();
            }
            else
            {
                var reconfiguredServiceNames = new List<string>();
                foreach (var serviceId in _activeSetup_ServicesBeingConfigured)
                {
                    var service = _availableServices.FirstOrDefault(s => s.Id == serviceId);
                    if (service != null)
                    {
                        reconfiguredServiceNames.Add(service.Name);
                        if (!string.IsNullOrEmpty(service.EnableConfigKey))
                        {
                            Console.WriteLine($"[InteractiveSetup] Reconfiguration of {service.Name} completed. Setting {service.EnableConfigKey} to true.");
                            JReader.OverwriteConfigValue(service.EnableConfigKey, true);
                        }
                    }
                }
                await JReader.GetStartingJsonAsync();

                completionMessage = "**Reconfiguration done**\n";
                completionMessage += "Selected settings have been updated.\n";
                if (reconfiguredServiceNames.Any())
                    completionMessage += $"Reconfigured: **{string.Join(", ", reconfiguredServiceNames)}**.\n";
                completionMessage +=
                    "You may need to restart the bot for some changes to take full effect if they involve tracker reinitialization (e.g. Discord Token change).";
            }


            if (_activeSetup_LastInteraction != null && _activeSetup_LastInteraction.HasResponded &&
                !(_activeSetup_LastInteraction is SocketModal))
            {
                if (isEphemeralContext || _activeSetup_LastPromptMessage == null)
                    await _activeSetup_LastInteraction.FollowupAsync(completionMessage, ephemeral: true);
                else if (_activeSetup_LastPromptMessage != null)
                    await _activeSetup_LastPromptMessage.ModifyAsync(m =>
                    {
                        m.Content = completionMessage;
                        m.Components = null;
                    });
            }
            else if (_activeSetup_LastPromptMessage != null)
            {
                await _activeSetup_LastPromptMessage.ModifyAsync(m =>
                {
                    m.Content = completionMessage;
                    m.Components = null;
                });
            }
            else
            {
                _activeSetup_LastPromptMessage = await channel.SendMessageAsync(completionMessage);
            }

            Console.WriteLine("[InteractiveSetup] setup done");
            ResetInteractiveSetupState();
            return;
        }

        var step = _activeSetup_PendingSteps.First();
        var cb = new ComponentBuilder().WithButton(step.ButtonText, $"setup_setval_{step.ConfigKey}",
            ButtonStyle.Success);

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
                $"setup_setdefault_{step.ConfigKey}", ButtonStyle.Secondary, row: 1);


        if (_activeSetup_LastInteraction != null && _activeSetup_LastInteraction.HasResponded &&
            !(_activeSetup_LastInteraction is SocketModal))
        {
            if (isEphemeralContext ||
                _activeSetup_LastPromptMessage == null)
            {
                await _activeSetup_LastInteraction.FollowupAsync(step.PromptMessage, components: cb.Build(),
                    ephemeral: isEphemeralContext);
                _activeSetup_LastPromptMessage =
                    null;
            }
            else if (_activeSetup_LastPromptMessage != null)
            {
                await _activeSetup_LastPromptMessage.ModifyAsync(m =>
                {
                    m.Content = step.PromptMessage;
                    m.Components = cb.Build();
                });
            }
        }
        else if (_activeSetup_LastPromptMessage != null && _activeSetup_LastPromptMessage.Author.Id == _client.CurrentUser.Id && _activeSetup_LastPromptMessage.Channel.Id == _activeSetup_ChannelId)
        {
            await _activeSetup_LastPromptMessage.ModifyAsync(m =>
            {
                m.Content = step.PromptMessage;
                m.Components = cb.Build();
            });
        }
        else
        {
            _activeSetup_LastPromptMessage = await channel.SendMessageAsync(step.PromptMessage, components: cb.Build());
        }
    }

    private void ResetInteractiveSetupState()
    {
        _isInteractiveSetupActive = false;
        _activeSetup_ChannelId = 0;
        _activeSetup_PendingSteps.Clear();
        _activeSetup_ServicesBeingConfigured.Clear();
        _activeSetup_LastPromptMessage = null;
        _activeSetup_LastInteraction = null;
    }

    private string GetDefaultValuePreview(string configKey)
    {
        var defaultConfig = new JReader.Config();
        object? defaultValueObj = null;

        switch (configKey)
        {
            case "generalBotLogChannelId":
            case "generalBotImportantChannelId":
                var setupChannelIdForDefault = JReader.CurrentConfig.generalBotSetupChannelId;
                if (setupChannelIdForDefault != 0)
                {
                    defaultValueObj = setupChannelIdForDefault.ToString();
                }
                else
                {
                    var prop = typeof(JReader.Config).GetProperty(configKey);
                    if (prop != null) defaultValueObj = prop.GetValue(defaultConfig);
                }

                break;
            default:
                var property = typeof(JReader.Config).GetProperty(configKey);
                if (property != null) defaultValueObj = property.GetValue(defaultConfig);
                break;
        }

        var preview = defaultValueObj?.ToString() ?? "N/A";
        if (preview.Length > 20) preview = preview.Substring(0, 17) + "...";
        if (string.IsNullOrWhiteSpace(preview)) preview = "[empty]";
        return preview;
    }

    private async Task HandleSetupModalInteractionAsync(SocketModal modal)
    {
        if (!modal.Data.CustomId.StartsWith("setup_modal_")) return;
        if (!_isInteractiveSetupActive || modal.Channel.Id != _activeSetup_ChannelId)
        {
            await modal.RespondAsync(
                "This modal is part of an active setup process and cannot be used outside of it, in this channel, or the process has expired.",
                ephemeral: true);
            return;
        }

        _activeSetup_LastInteraction = modal;

        var configKey = modal.Data.CustomId.Replace("setup_modal_", "");
        var inputValue = modal.Data.Components.FirstOrDefault(c => c.CustomId == "input_value")?.Value;
        if (inputValue == null)
        {
            await modal.RespondAsync("Err: Modal input value not found", ephemeral: true);
            return;
        }


        var step = _activeSetup_PendingSteps.FirstOrDefault(s => s.ConfigKey == configKey);
        if (step == null)
        {
            await modal.RespondAsync("Err: Original setup step not found in the pending list", ephemeral: true);
            return;
        }

        (bool isValid, string? errorMessage, object? parsedValue) validationResult = (false, "Validator not set", null);
        if (step.Validator != null)
            validationResult = step.Validator(inputValue);
        else
            validationResult = (true, null, inputValue);


        if (!validationResult.isValid)
        {
            await modal.RespondAsync(
                $"Invalid input for '{step.ModalTitle}': {validationResult.errorMessage}\nPlease click '{step.ButtonText}' again to retry",
                ephemeral: true);
            return;
        }

        var valueToSet = validationResult.parsedValue ?? inputValue;


        var success = JReader.OverwriteConfigValue(configKey, valueToSet);
        if (success)
        {
            _activeSetup_PendingSteps.Remove(step);
            await modal.RespondAsync($"{step.ModalTitle} has been set to `{valueToSet}`", ephemeral: true);
            await ProcessNextSetupStepAsync();
        }
        else
        {
            await modal.RespondAsync(
                $"Failed to set {step.ModalTitle} Please check logs (/debug) and try again via the button",
                ephemeral: true);
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

        if (IsInteractiveSetupActive() && _activeSetup_ChannelId != 0 && effectiveChannelId == _activeSetup_ChannelId)
            Console.WriteLine($"[SendBotMessage] Setup active on channel {_activeSetup_ChannelId}");
        if (!CanSendMessages())
        {
            Console.WriteLine("bot timed out (paused)... message not sent");
            return;
        }


        if (channelId == 0)
        {
            channelId = effectiveChannelId;
            if (channelId == 0)
            {
                var errorFallbackChannel = IsInteractiveSetupActive()
                    ? _activeSetup_ChannelId
                    : JReader.CurrentConfig.generalBotSetupChannelId;
                if (errorFallbackChannel != 0)
                    //MethodMsgSetup($"Could not determine channel for logLevel {logLevel} cfg value might be missing or 0", errorFallbackChannel, 1);
                    Console.WriteLine($"[SendBotMessage] Could not determine channel for logLevel {logLevel} cfg value might be missing or 0");
                // idk here but I assume we simply can set it here as a last resort instead of entirely exiting, errorFallbackChannel fails sometimes gotta figure out why tho
                channelId = JReader.CurrentConfig.generalBotSetupChannelId;
            }
        }

        switch (logLevel)
        {
            case 0:
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
                var errorFallbackChannelDef = IsInteractiveSetupActive()
                    ? _activeSetup_ChannelId
                    : JReader.CurrentConfig.generalBotSetupChannelId;
                if (errorFallbackChannelDef != 0)
                    MethodMsgSetup($"logLevel was either out of bounds (0-3). Received: {logLevel}", errorFallbackChannelDef, 1);
                Console.WriteLine($"[SendBotMessage] logLevel was out of bounds (0-3). Received: {logLevel}");
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
            Console.WriteLine("[SendMsgEmbed] Bot not connected = msg not sent");
            return;
        }

        var effectiveChannelId = channelId == 0 ? DetermineChannelFromLogLevel(logLevel) : channelId;

        if (effectiveChannelId == 0)
        {
            Console.WriteLine(
                $"[SendMsgEmbed] Failed to determine channel for logLevel {logLevel} cfg values missing?");
            return;
        }

        if (IsInteractiveSetupActive() && _activeSetup_ChannelId != 0 && effectiveChannelId == _activeSetup_ChannelId)
            Console.WriteLine($"[SendMsgEmbed] Setup active on channel {_activeSetup_ChannelId}");

        var channel = await _client.GetChannelAsync(Convert.ToUInt64(effectiveChannelId)) as ITextChannel;

        if (channel != null)
        {
            if (!CanSendMessages())
            {
                Console.WriteLine("bot timed out (paused)... embed message not sent");
                return;
            }

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
        if (channelId == 0)
        {
            Console.WriteLine($"[MethodMsgSetup] Target channelId is 0");
            // ADD MB
            return;
        }


        var channel = await _client.GetChannelAsync(Convert.ToUInt64(channelId)) as ITextChannel;

        if (channel != null)
        {
            if (!CanSendMessages() && type != 1)
            {
                Console.WriteLine("bot timed out (paused)... setup message not sent");
                return;
            }

            switch (type)
            {
                case 0:
                    await channel.SendMessageAsync($"Hi! This is the 1st message on startup from the bot! Use /startinfo to get further information. If its the 1st startup it might take time for the slash commands to take effect.\nTrack Discord Messages: {JReader.CurrentConfig.trackDiscord}, Track Roblox: {JReader.CurrentConfig.trackRoblox}.\nTracking will begin in shortly for the activated...");
                    break;
                case 1:
                    await channel.SendMessageAsync(payload);
                    break;
                default:
                    await channel.SendMessageAsync("Unknown call received");
                    break;
            }
        }
        else
        {
            Console.WriteLine($"[MethodMsgSetup] Target channel {channelId} not found or is not a text channel");
        }
    }

    private async Task MethodMsgLog(string payload, bool header, ulong channelId)
    {
        if (_client.ConnectionState != ConnectionState.Connected) return;

        var channel = await _client.GetChannelAsync(Convert.ToUInt64(channelId)) as ITextChannel;

        if (channel != null)
        {
            if (!CanSendMessages())
            {
                Console.WriteLine("bot timed out (paused)... log message not sent.");
                return;
            }

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
            Console.WriteLine($"[MethodMsgLog] channel {channelId} not found");
        }
    }

    private async Task MethodMsgImportant(string payload, bool header, ulong channelId, bool desktopNotify = false)
    {
        if (_client.ConnectionState != ConnectionState.Connected) return;

        if (desktopNotify)
        {
            // TODO: COOK THIS UP
        }

        var channel = await _client.GetChannelAsync(Convert.ToUInt64(channelId)) as ITextChannel;

        if (channel != null)
        {
            if (!CanSendMessages())
            {
                Console.WriteLine("bot timed out (paused)... important message not sent.");
                return;
            }

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
            if (string.IsNullOrWhiteSpace(pingTarget) || pingTarget.Equals(new JReader.Config().generalWhoToPing, StringComparison.OrdinalIgnoreCase))
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

public class TrackableService
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string? EnableConfigKey { get; set; }
    public List<SetupStep> SetupSteps { get; set; } = new();
    public bool IsCoreSetting { get; set; } = false;
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

    // returns: IsValid, ErrorMessage (if !IsValid), ParsedValue
    public Func<string, (bool isValid, string? errorMessage, object? parsedValue)>? Validator { get; set; } =
        DefaultValidator;

    // validator for shrimple str inputs or when specific validation isnt needed at step level
    private static (bool, string?, object?) DefaultValidator(string input)
    {
        return (true, null, input);
    }
}
