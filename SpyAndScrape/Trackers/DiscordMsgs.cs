using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpyAndScrape.config;

namespace SpyAndScrape.Trackers
{
    public class DiscordMsgs
    {
        private Process _pyProcess;
        private ClientWebSocket _clientWebSocket;
        private readonly NotifierBot _notifierBot;

        public DiscordMsgs(NotifierBot notifierBot)
        {
            _notifierBot = notifierBot;
        }

        public async Task StartPyWss()
        {
            string pyPath = GetPyPath();
            if (string.IsNullOrEmpty(pyPath))
            {
                Console.WriteLine("python not found.");
                _notifierBot.SendBotMessage("Python was not found, to track discord please install python.", 3, false);
                JReader.OverwriteConfigValue("trackDiscord", "false");
                return;
            }

            string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "discordWss.py");
            Console.WriteLine($"python Path: {pyPath}");
            Console.WriteLine($"py script Path: {scriptPath}");

            
            if (!File.Exists(scriptPath))
            {
                Console.WriteLine("py file not found locally, attempt to download...");

                string gUrl = "https://raw.githubusercontent.com/arti4ikmin/PythonQuick/refs/heads/main/discordWss.py";


                using (HttpClient c = new HttpClient())
                {
                    string scriptContent = await c.GetStringAsync(gUrl);
                    await File.WriteAllTextAsync(scriptPath, scriptContent);
                }

            }
            
            _pyProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pyPath,
                    Arguments = $"-u \"{scriptPath}\"",  // -u for unbuffered output!!!
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };


            _pyProcess.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
            _pyProcess.ErrorDataReceived += (sender, args) => Console.WriteLine("PYTHON: error: " + args.Data + "\n if error states that cannot have 2 same wss at the same time, try terminating any python process in task manager.");
            _pyProcess.Start();
            _pyProcess.BeginOutputReadLine();
            _pyProcess.BeginErrorReadLine();

            Console.WriteLine("py ws server started.");

            await ConnectToPyWss();

        }

        public void StopPyWss()
        {

            
            // kill the py if its still running
            if (!_pyProcess.HasExited)
            {
                _pyProcess.Kill();
                _pyProcess.Dispose();
                Console.WriteLine("python process killed.");
            }
            if (_clientWebSocket.State == WebSocketState.Open || _clientWebSocket.State == WebSocketState.Connecting)
            {
                _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "App is exiting", CancellationToken.None).Wait();
            }
            _clientWebSocket.Dispose();
            Console.WriteLine("websocket connection closed.");
               
        }

        static string GetPyPath()
        {

            var proStartInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "python",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(proStartInfo))
            using (var reader = proc.StandardOutput)
            {
                return reader.ReadLine();
            }
            
        }

        async Task ConnectToPyWss()
        {
            using (var clientWs = new ClientWebSocket())
            {
                Uri serverUri = new Uri("ws://localhost:8765");
                var tmp = 0;
                while (tmp <= 1)
                {
                    try
                    {
                        Console.WriteLine("attempting to connect to py ws server...");
                        await clientWs.ConnectAsync(serverUri, CancellationToken.None);
                        Console.WriteLine("connected to py wss server.");
                        tmp = 0;
                        
                        await ListenForMessages(clientWs);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"wss error: {ex.Message}");
                        Console.WriteLine("reconnecting in 2 seconds...");
                        tmp++;
                        
                        await Task.Delay(2000);
                    }
                }
            }
        }

        private async Task ListenForMessages(ClientWebSocket clientWs)
        {
            // increase maybe sometime or enough?
            byte[] buffer = new byte[2048];

            while (clientWs.State == WebSocketState.Open)
            {
                WebSocketReceiveResult res = await clientWs.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (res.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, res.Count);
                    ProcessPayload(message);
                }
                else if (res.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine("server closed the wss connection.");
                    await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
            }
        }

        private void ProcessPayload(string payload)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Payload>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true,
                    AllowTrailingCommas = true // scary

                });
                if (data?.D != null)
                {
                    var attachmentDetails = ExtractAttachments(data.D.Attachments);
                    Console.WriteLine($"Received message data: {JsonSerializer.Serialize(data.D, new JsonSerializerOptions { WriteIndented = true })}");
                    //Console.WriteLine($"Data: \n Was sent at: {data.D.Timestamp.ToString("o")} \n Message: {JsonSerializer.Serialize(data.D.Content)} \n Jump to message: https://discord.com/channels/{data.D.GuildId}/{data.D.ChannelId}/{data.D.Id}");
                    _notifierBot.SendBotMessage(
                        $"** Data: ** \n" +
                        $"*Was sent on:* {data.D.Timestamp.ToString("dddd, MMMM dd, yyyy h:mm tt")} \n" +
                        $"*Message:* {JsonSerializer.Serialize(data.D.Content)} \n" +
                        $"{attachmentDetails} \n" +
                        $"Jump to message: " +
                        $"https://discord.com/channels/{data.D.GuildId}/{data.D.ChannelId}/{data.D.Id}"
                    , JReader.CurrentConfig.discordTrackingLogLevel);
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"err payload: {ex.Message}");
                Console.WriteLine($"payload: {payload}");
            }
        }
        
        // extract and format the attachment details
        string ExtractAttachments(JsonElement attachments)
        {
            StringBuilder attachmentDetails = new StringBuilder();
    
            if (attachments.ValueKind == JsonValueKind.Array)
            {
                foreach (var attachment in attachments.EnumerateArray())
                {
                    string? filename = attachment.GetProperty("filename").GetString();
                    string? url = attachment.GetProperty("url").GetString();
                    string? contentType = attachment.GetProperty("content_type").GetString();
                    // omg I just discovered the ?? here, theyre so useful
                    attachmentDetails.AppendLine("*Attachments:* ");
                    attachmentDetails.AppendLine($" - Type: {contentType}");
                    attachmentDetails.AppendLine($" - Filename: {filename}");
                    attachmentDetails.AppendLine($" - File: {url}");
                    attachmentDetails.AppendLine();
                }
            }
            else
            {
                attachmentDetails.AppendLine("");
            }
    
            return attachmentDetails.ToString();
        }
    }

    public class Payload
    {
        [JsonPropertyName("d")]
        public PayloadData D { get; set; }
    }

    
    // this was intended to be a bigger usage, had to make a whole class...
    public class PayloadData
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
        public string GuildId { get; set; }
    }
}
