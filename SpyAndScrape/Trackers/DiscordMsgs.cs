using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpyAndScrape.config;

#pragma warning disable 4014

namespace SpyAndScrape.Trackers
{
    internal class GatewayPayload
    {
        [JsonPropertyName("op")]
        public int Op { get; set; }

        [JsonPropertyName("d")]
        public JsonElement D { get; set; }

        [JsonPropertyName("s")]
        public int? S { get; set; }

        [JsonPropertyName("t")]
        public string? T { get; set; }
    }

    internal class HelloPayload
    {
        [JsonPropertyName("heartbeat_interval")]
        public int HeartbeatInterval { get; set; }
    }

    internal class IdentifyProperties
    {
        [JsonPropertyName("$os")]
        public string Os { get; set; } = "windows";

        [JsonPropertyName("$browser")]
        public string Browser { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";

        [JsonPropertyName("$device")]
        public string Device { get; set; } = "Windows";
    }
    
    internal class IdentifyPayloadData
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("properties")]
        public IdentifyProperties Properties { get; set; } = new IdentifyProperties();
        // Example: [JsonPropertyName("intents")] public int Intents { get; set; } = 513;
    }

    internal class ReadyPayload
    {
        [JsonPropertyName("v")]
        public int Version { get; set; }
        [JsonPropertyName("user")]
        public JsonElement User { get; set; } 
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; }
        [JsonPropertyName("resume_gateway_url")]
        public string ResumeGatewayUrl { get; set; }
    }
    
    internal class ResumePayloadData
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; }
        [JsonPropertyName("seq")]
        public int Seq { get; set; }
    }

    internal class DiscordUserAuthor
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("username")]
        public string Username { get; set; }
    }

    internal class DiscordMessageData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("embeds")]
        public JsonElement Embeds { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("channel_id")]
        public string ChannelId { get; set; }

        [JsonPropertyName("attachments")]
        public JsonElement Attachments { get; set; }

        [JsonPropertyName("guild_id")]
        public string? GuildId { get; set; } // nullable for DMs

        [JsonPropertyName("author")]
        public DiscordUserAuthor Author { get; set; }
        
    }


    public class DiscordMsgs
    {
        private readonly NotifierBot _notifierBot;
        private ClientWebSocket _gatewaySocket;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _heartbeatTask;
        
        private int _heartbeatIntervalMs;
        private int? _lastSequenceNumber; // nullable for init state
        private string _sessionId;
        private string _resumeGatewayUrl;

        private string _discordToken;
        private string _trackingUsername;

        private const string DefaultDiscordGatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };


        public DiscordMsgs(NotifierBot notifierBot)
        {
            _notifierBot = notifierBot;
        }

        public async Task StartTrackingAsync()
        {
            _discordToken = JReader.CurrentConfig.discordTrackingToken;
            _trackingUsername = JReader.CurrentConfig.discordTrackingUsername;

            if (string.IsNullOrEmpty(_discordToken) || string.IsNullOrEmpty(_trackingUsername))
            {
                Console.WriteLine("[DiscordMsgs] Token or tracking username is not configured. Discord tracking will not start.");
                _notifierBot.SendBotMessage("Discord tracking token or username not set in config.json. Tracking disabled.", 3, false);
                JReader.OverwriteConfigValue("trackDiscord", "false"); // if misconfigured
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            Console.WriteLine("[DiscordMsgs] Starting Discord tracking...");

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    _gatewaySocket = new ClientWebSocket();
                    string targetGatewayUrl = _resumeGatewayUrl ?? DefaultDiscordGatewayUrl;
                    
                    Console.WriteLine($"[DiscordMsgs] Attempting to connect to Discord Gateway: {targetGatewayUrl}");
                    await _gatewaySocket.ConnectAsync(new Uri(targetGatewayUrl), _cancellationTokenSource.Token);
                    Console.WriteLine("[DiscordMsgs] Connected to Discord Gateway.");

                    await ReceiveMessagesAsync(_cancellationTokenSource.Token);
                }
                catch (WebSocketException ex)
                {
                    Console.WriteLine($"[DiscordMsgs] WebSocketException: {ex.Message} Attempting to reconnect...");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[DiscordMsgs] Tracking was cancelled.");
                    break; 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DiscordMsgs] Error during Discord tracking: {ex.Message} Attempting to reconnect...");
                }
                finally
                {
                    _gatewaySocket?.Dispose();
                    _gatewaySocket = null;
                    if (_heartbeatTask != null && !_heartbeatTask.IsCompleted)
                    {
                        _heartbeatTask.Wait(TimeSpan.FromSeconds(5));
                    }
                    _heartbeatTask = null; 
                }

                if (!_cancellationTokenSource.IsCancellationRequested)
                {
                    int reconnectDelay = new Random().Next(5000, 15000); // rand delay between 5-15 seconds
                    Console.WriteLine($"[DiscordMsgs] Reconnecting in {reconnectDelay / 1000} seconds...");
                    await Task.Delay(reconnectDelay, _cancellationTokenSource.Token);
                }
            }
            Console.WriteLine("[DiscordMsgs] Discord tracking stopped");
        }

        public async Task StopTrackingAsync()
        {
            Console.WriteLine("[DiscordMsgs] Stopping Discord tracking...");
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            if (_gatewaySocket != null && (_gatewaySocket.State == WebSocketState.Open || _gatewaySocket.State == WebSocketState.Connecting))
            {
                try
                {
                    await _gatewaySocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client requested disconnect", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DiscordMsgs] Exception during WebSocket close: {ex.Message}");
                }
            }
            _gatewaySocket?.Dispose();
            _gatewaySocket = null;

            if (_heartbeatTask != null)
            {
                try
                {
                    await Task.WhenAny(_heartbeatTask, Task.Delay(TimeSpan.FromSeconds(2)));
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[DiscordMsgs] Exception waiting for heartbeat task: {ex.Message}");
                }
                _heartbeatTask = null;
            }
            Console.WriteLine("[DiscordMsgs] Discord tracking stopped");
        }

        private async Task ReceiveMessagesAsync(CancellationToken token)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]); // 8KB

            while (_gatewaySocket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                using (var ms = new MemoryStream())
                {
                    do
                    {
                        result = await _gatewaySocket.ReceiveAsync(buffer, token);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    } while (!result.EndOfMessage);

                    ms.Seek(0, SeekOrigin.Begin);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(ms.ToArray());
                        // Console.WriteLine($"[DiscordMsgs] Raw << {message}");
                        await HandleGatewayMessageAsync(message, token);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"[DiscordMsgs] Gateway closed connection: {result.CloseStatusDescription}");
                        await _gatewaySocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        return;
                    }
                }
            }
        }
        
        private async Task HandleGatewayMessageAsync(string jsonMessage, CancellationToken token)
        {
            try
            {
                var gatewayPayload = JsonSerializer.Deserialize<GatewayPayload>(jsonMessage, JsonOptions);

                if (gatewayPayload.S.HasValue)
                {
                    _lastSequenceNumber = gatewayPayload.S.Value;
                }

                switch (gatewayPayload.Op)
                {
                    case 0: // Dispatch
                        await HandleDispatchAsync(gatewayPayload.T, gatewayPayload.D, token);
                        break;
                    case 1: // Heartbeat Request
                        Console.WriteLine("[DiscordMsgs] Heartbeat Request received. Sending Heartbeat.");
                        await SendHeartbeatAsync(token);
                        break;
                    case 7: // Reconnect
                        Console.WriteLine("[DiscordMsgs] Gateway requested Reconnect. Closing and will attempt to reconnect.");
                        await _gatewaySocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Reconnect requested", token);
                        // outer loop will handle reconnection
                        break;
                    case 9: // Invalid Session
                        bool resumable = gatewayPayload.D.GetBoolean();
                        Console.WriteLine($"[DiscordMsgs] Invalid Session. Resumable: {resumable}. Re-identifying.");
                        if (!resumable)
                        {
                            _sessionId = null; // for full reidentify
                            _lastSequenceNumber = null;
                        }
                        await Task.Delay(new Random().Next(1000, 5000), token);
                        await SendIdentifyAsync(token);
                        break;
                    case 10: // hi
                        var hello = JsonSerializer.Deserialize<HelloPayload>(gatewayPayload.D.GetRawText(), JsonOptions);
                        _heartbeatIntervalMs = hello.HeartbeatInterval;
                        Console.WriteLine($"[DiscordMsgs] Hello received. Heartbeat interval: {_heartbeatIntervalMs}ms");
                        
                        _heartbeatTask = HeartbeatLoopAsync(token);

                        if (!string.IsNullOrEmpty(_sessionId) && _lastSequenceNumber.HasValue)
                        {
                            Console.WriteLine("[DiscordMsgs] Attempting to resume session.");
                            await SendResumeAsync(token);
                        }
                        else
                        {
                            Console.WriteLine("[DiscordMsgs] Sending Identify.");
                            await SendIdentifyAsync(token);
                        }
                        break;
                    case 11: // HB ACK
                        // Console.WriteLine("[DiscordMsgs] Heartbeat ACK received.");
                        break;
                    default:
                        Console.WriteLine($"[DiscordMsgs] Unknown Opcode: {gatewayPayload.Op}");
                        break;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[DiscordMsgs] Error deserializing Gateway message: {ex.Message}. Payload: {jsonMessage}");
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"[DiscordMsgs] Error handling Gateway message: {ex.ToString()}. Payload: {jsonMessage}");
            }
        }

        private async Task HandleDispatchAsync(string eventType, JsonElement eventData, CancellationToken token)
        {
            // Console.WriteLine($"[DiscordMsgs] Dispatch event: {eventType}");
            switch (eventType)
            {
                case "READY":
                    var ready = JsonSerializer.Deserialize<ReadyPayload>(eventData.GetRawText(), JsonOptions);
                    _sessionId = ready.SessionId;
                    _resumeGatewayUrl = ready.ResumeGatewayUrl; // This might include /?v=X&encoding=json
                    Console.WriteLine($"[DiscordMsgs] READY received. Session ID: {_sessionId}. Resume URL: {_resumeGatewayUrl}");
                    // User info: ready.User (JsonElement)
                    break;
                case "RESUMED":
                    Console.WriteLine("[DiscordMsgs] Successfully RESUMED session.");
                    break;
                case "MESSAGE_CREATE":
                    var msg = JsonSerializer.Deserialize<DiscordMessageData>(eventData.GetRawText(), JsonOptions);
                    if (msg?.Author?.Username == _trackingUsername)
                    {
                        ProcessDiscordMessage(msg);
                    }
                    break;
                default:
                    Console.WriteLine($"[DiscordMsgs] Idk event type: {eventType}");
                    break;
            }
            await Task.CompletedTask;
        }
        
        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            // heartbeat immediately after identify/resume based on Discord docs jitter
            // first hb after interval * jitter (random fraction between 0 and 1)
            await Task.Delay((int)(_heartbeatIntervalMs * new Random().NextDouble()), token);

            while (!token.IsCancellationRequested && _gatewaySocket.State == WebSocketState.Open)
            {
                try
                {
                    await SendHeartbeatAsync(token);
                    await Task.Delay(_heartbeatIntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[DiscordMsgs] Heartbeat loop cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DiscordMsgs] Error in heartbeat loop: {ex.Message}");
                    await Task.Delay(5000, token);
                }
            }
             Console.WriteLine("[DiscordMsgs] Heartbeat loop ended");
        }

        private Task SendHeartbeatAsync(CancellationToken token)
        {
            // Console.WriteLine("[DiscordMsgs] Sending Heartbeat.");
            return SendJsonAsync(new { op = 1, d = _lastSequenceNumber }, token);
        }

        private Task SendIdentifyAsync(CancellationToken token)
        {
            var identifyPayload = new IdentifyPayloadData
            {
                Token = _discordToken
            };
            return SendJsonAsync(new { op = 2, d = identifyPayload }, token);
        }

        private Task SendResumeAsync(CancellationToken token)
        {
            var resumePayload = new ResumePayloadData
            {
                Token = _discordToken,
                SessionId = _sessionId,
                Seq = _lastSequenceNumber ?? 0
            };
            Console.WriteLine($"[DiscordMsgs] Sending Resume: Token: ..., SessionId: {_sessionId}, Seq: {_lastSequenceNumber}");
            return SendJsonAsync(new { op = 6, d = resumePayload }, token);
        }

        private async Task SendJsonAsync(object payload, CancellationToken token)
        {
            if (_gatewaySocket.State != WebSocketState.Open)
            {
                Console.WriteLine("[DiscordMsgs] Cannot send JSON, WebSocket is not open.");
                return;
            }
            try
            {
                string jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);
                // Console.WriteLine($"[DiscordMsgs] Raw >> {jsonPayload}")
                byte[] Rbytes = Encoding.UTF8.GetBytes(jsonPayload);
                await _gatewaySocket.SendAsync(new ArraySegment<byte>(Rbytes), WebSocketMessageType.Text, true, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiscordMsgs] Error sending JSON: {ex.Message}");
            }
        }
        
        private void ProcessDiscordMessage(DiscordMessageData discordMsg)
        {
            try
            {
                var attachmentDetails = ExtractAttachments(discordMsg.Attachments);
                string jumpLink = $"https://discord.com/channels/{(discordMsg.GuildId ?? "@me")}/{discordMsg.ChannelId}/{discordMsg.Id}";
                
                StringBuilder messageContent = new StringBuilder();
                messageContent.AppendLine($"**Data from {discordMsg.Author.Username}:**");
                messageContent.AppendLine($"*Sent on:* {discordMsg.Timestamp.ToLocalTime().ToString("dddd, MMMM dd, yyyy h:mm:ss tt K")}");
                messageContent.AppendLine($"*Message:* {discordMsg.Content}");
                if (!string.IsNullOrWhiteSpace(attachmentDetails))
                {
                     messageContent.AppendLine(attachmentDetails);
                }
                messageContent.AppendLine($"Jump to message: {jumpLink}");
                
                Console.WriteLine($"[DiscordMsgs] Tracked message from {discordMsg.Author.Username}: {discordMsg.Content.Substring(0, Math.Min(50, discordMsg.Content.Length))}{(discordMsg.Content.Length > 50 ? "..." : "")}");

                _notifierBot.SendBotMessage(
                    messageContent.ToString(),
                    JReader.CurrentConfig.discordTrackingLogLevel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DiscordMsgs] Error processing Discord message: {ex.Message}. Data: {JsonSerializer.Serialize(discordMsg, JsonOptions)}");
            }
        }
        
        string ExtractAttachments(JsonElement attachmentsElement)
        {
            StringBuilder attachmentDetails = new StringBuilder();
    
            if (attachmentsElement.ValueKind == JsonValueKind.Array && attachmentsElement.GetArrayLength() > 0)
            {
                attachmentDetails.AppendLine("*Attachments:* ");
                foreach (var attachment in attachmentsElement.EnumerateArray())
                {
                    string filename = attachment.TryGetProperty("filename", out var fn) ? fn.GetString() : "N/A";
                    string url = attachment.TryGetProperty("url", out var u) ? u.GetString() : "N/A";
                    string contentType = attachment.TryGetProperty("content_type", out var ct) ? ct.GetString() : "N/A";
                    
                    attachmentDetails.AppendLine($" - Type: {contentType}");
                    attachmentDetails.AppendLine($" - Filename: {filename}");
                    attachmentDetails.AppendLine($" - File: {url}");
                }
            }
            return attachmentDetails.ToString();
        }
    }
}