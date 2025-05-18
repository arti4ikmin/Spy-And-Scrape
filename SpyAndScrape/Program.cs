// MIT License
//
// Copyright (c) 2024 Arti
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, subject to the following conditions:
//
// 1. The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// 2. Users must adhere to all applicable laws and regulations when using the Software.
//    The Software must not be used for any illegal activities, including but not limited to stalking or harassment.
//
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
//     INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
//     IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
//     TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.



// My Note:
//     THE MAIN INTENTION OF THE SOFTWARE WAS EDUCATIONAL PURPOSES AS WELL TRAINING FOR FUTURE PROJECTS.

// Unless critical bugs or direct requests, it might be the latest version of this software. Some parts like the json comparer or wss server might be reused in future projects.


// If you want to read the code lets clear smth up: very often the naming is shortened, e.g. the JCmp stands for JsonComparer. Should be understandable from the context


using System.Diagnostics;
using System.Text;
using SpyAndScrape.config;
using SpyAndScrape.Trackers;

#pragma warning disable 4014

namespace SpyAndScrape
{
    class Program
    {
        private static DiscordMsgs _dcMsgs;
        private static HttpClient _httpClient;
        private static NotifierBot _bot;
        private static StreamWriter _logWriter;
        private static TextWriter _origConsoleOutput;
        private static bool _isLogging = false;
        private static NotifyIcon _notifyIcon;
        
        private static string PromptInput(string title, string prompt, bool isPass = false)
        {
            // [STAThread] is critical
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                string result = null;
                var t = new Thread(() =>
                {
                    Application.EnableVisualStyles();
                    using (var dialog = new InputDialog(title, prompt, isPass))
                    {
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            result = dialog.InputValue;
                        }
                    }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
                return result;
            }
            else
            {
                Application.EnableVisualStyles();
                using (var dialog = new InputDialog(title, prompt, isPass))
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        return dialog.InputValue;
                    }
                    return null;
                }
            }
        }

        // 
        [STAThread]
        static async Task Main(string[] args)
        {
            CommandLineArgs.SetArgs(args);
            //MessageBox.Show("WELCOME TO SPY AND SCRAPE. CONSOLE DISPLAYS A LOT OF SHITPOSTS FOR DEBUG.");
            Console.WriteLine("WELCOME TO SPY AND SCRAPE. CONSOLE DISPLAYS A LOT OF SHITPOSTS FOR DEBUG. \n \n");

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine($"[Unobserved Task ex] An err was caught: {e.Exception.Message}. Details: {e.Exception.StackTrace}");
                e.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = (Exception)e.ExceptionObject;
                Console.WriteLine($"[UNHANDLED ex] An error was caught: {ex.Message}. Details: {ex.StackTrace}");
                // System.Windows.Forms.MessageBox.Show($"A critical unhandled error occurred: {ex.Message}\nThe application will now exit.", "Unhandled Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
                PerformShutdownTasks();
                _notifyIcon?.Dispose();
                StopLogging();
                Environment.Exit(1);
            };

            try
            {
                StartLogging();
                Console.WriteLine("SPY AND SCRAPE: app starting...");

                InitializeNotifyIcon();
                
                if (args.Length > 0 && args[0] == "delay")
                {
                    Console.WriteLine("THE PROGRAM WAS STARTED WITH DELAY ARGUMENT, WAITING 1.5 SECONDS");
                    await Task.Delay(1500);
                }
                if (args.Length == 0)
                {
                    Console.WriteLine("Program was init without args");
                }

                _httpClient = new HttpClient();
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                await JReader.GetStartingJsonAsync();
            Checks:
                Thread.Sleep(100);

                if (string.IsNullOrEmpty(JReader.CurrentConfig.generalBotToken))
                {
                    Console.WriteLine("CRITICAL: No bot token provided. Prompting user.");
                    var tmpval = PromptInput("Bot Token Required", "CRITICAL: No bot token provided.\nPlease enter the Discord Bot Token:", true);

                    if (string.IsNullOrWhiteSpace(tmpval))
                    {
                        Console.WriteLine("No bot token input was given by the user. Application cannot continue.");
                        MessageBox.Show("A Discord Bot Token is required to run the application. Exiting.", "Critical Configuration Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        PerformShutdownTasks(minimal: true);
                        StopLogging();
                        Environment.Exit(1);
                        return;
                    }
                    JReader.OverwriteConfigValue("generalBotToken", tmpval);
                    await JReader.GetStartingJsonAsync();
                }

                if (JReader.CurrentConfig.generalBotSetupChannelId == 0)
                {
                    Console.WriteLine("CRITICAL: No setup channel ID provided. Prompting user.");
                    var tmpval = PromptInput("Setup Channel ID Required", "CRITICAL: Please provide the starting channel ID for the bot (e.g., for setup messages):");

                    if (string.IsNullOrWhiteSpace(tmpval) || !ulong.TryParse(tmpval, out ulong result))
                    {
                        Console.WriteLine("Invalid or no setup channel ID input was given by the user. Application cannot continue.");
                        MessageBox.Show("A valid starting Bot Setup Channel ID is required. Exiting.", "Critical Configuration Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        PerformShutdownTasks(minimal: true);
                        StopLogging();
                        Environment.Exit(1);
                        return;
                    }

                    JReader.OverwriteConfigValue("generalBotSetupChannelId", result);
                    if (JReader.CurrentConfig.generalBotLogChannelId == 0) JReader.OverwriteConfigValue("generalBotLogChannelId", result);
                    if (JReader.CurrentConfig.generalBotImportantChannelId == 0) JReader.OverwriteConfigValue("generalBotImportantChannelId", result);
                    await JReader.GetStartingJsonAsync();
                }

                _bot = new NotifierBot();

                var rbTrack = new RobloxTrack();
                if (JReader.CurrentConfig.trackRoblox)
                {
                     _ = Task.Run(() => rbTrack.StartTrackingRoblox(_bot));
                }


                if (JReader.CurrentConfig.trackDiscord == true)
                {
                    _dcMsgs = new DiscordMsgs(_bot);
                     _ = Task.Run(() => _dcMsgs.StartTrackingAsync());
                }

                Console.WriteLine("Starting Discord Bot...");
                await _bot.StartAsync(JReader.CurrentConfig.generalBotToken);
                // StartAsync has Task.Delay(-1) it will keep Main alive
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL MAIN EXCEPTION] An error occurred during startup/main execution: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"A critical error occurred: {ex.Message}\nThe application will attempt to shut down.", "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Console.WriteLine("Application is attempting to shut down or has completed its main task.");
                PerformShutdownTasks();
                _notifyIcon?.Dispose();
                StopLogging();
                Console.WriteLine("Application shutdown complete. Process will exit.");
            }
        }
        
        private static void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NotifyIcon] err loading icon: {ex.Message}");
            }

            _notifyIcon.Text = "SpyAndScrape";
            _notifyIcon.Visible = true;

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open Log File", null, OnOpenLogClickedHandler);
            contextMenu.Items.Add("Restart", null, OnRestartClickedHandler);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, OnExitClickedHandler);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // _notifyIcon.DoubleClick += (sender, args) => OnOpenLogClickedHandler(sender, args);
        }
        
        private static void OnOpenLogClickedHandler(object sender, EventArgs e)
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console_output.txt");
            Process.Start(new ProcessStartInfo(logFilePath) { UseShellExecute = true });
        }

        private static void OnRestartClickedHandler(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Are you sure you want to restart SpyAndScrape?", "SpyAndScrape - Restart Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                Console.WriteLine("[NotifyIcon] Restart requested by user.");
                _notifyIcon?.Dispose();
                _notifyIcon = null;

                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                try
                {
                    Process.Start(exePath, "delay");
                    PerformShutdownTasks(minimal: true);
                    StopLogging();
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to restart the application: {ex.Message}", "SpyAndScrape - Restart Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (_notifyIcon != null) InitializeNotifyIcon();
                }
            }
        }

        private static void OnExitClickedHandler(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Are you sure you want to exit SpyAndScrape?", "SpyAndScrape - Exit Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                Console.WriteLine("[NotifyIcon] Exit requested by user.");
                // main fin block will handle PerformShutdownTasks, etc
                Environment.Exit(0);
            }
        }
        
        private static void PerformShutdownTasks(bool minimal = false)
        {
            Console.WriteLine("Performing shutdown tasks...");
            _httpClient?.Dispose();

            if (!minimal) // full shutdown if not an early exit due to config
            {
                if (_dcMsgs != null)
                {
                    Console.WriteLine("Stopping Discord message tracking...");
                    _dcMsgs.StopTrackingAsync().Wait(TimeSpan.FromSeconds(5));
                }
                if (_bot != null)
                {
                     Console.WriteLine("Shutting down Discord bot...");
                    _bot.ShutdownBot().Wait(TimeSpan.FromSeconds(5));
                }
            }
            Console.WriteLine("Core shutdown tasks completed");
        }


        private static void StartLogging()
        {
            if (_isLogging) return;
            string lFP = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console_output.txt");
            if (File.Exists(lFP))
            {
                // string backupLogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"console_output_{DateTime.Now:yyyyMMddHHmmss}.txt.old");
                // File.Move(lFP, backupLogFile);
                File.Delete(lFP);
            }


            _origConsoleOutput = Console.Out;
            try
            {
                _logWriter = new StreamWriter(lFP, true) { AutoFlush = true };
                Console.SetOut(new MultiWriter(_origConsoleOutput, _logWriter));
                _isLogging = true;
                Console.WriteLine($"------[INFO] Logging started: {DateTime.Now} ------");
            }
            catch (Exception ex)
            {
                if (_origConsoleOutput != null) Console.SetOut(_origConsoleOutput);
                _isLogging = false;
                Debug.WriteLine($"[StartLogging Error] Failed to initialize StreamWriter: {ex.Message}");
                Console.WriteLine($"[StartLogging Error] Failed to initialize StreamWriter: {ex.Message}");
            }
        }
        private static void StopLogging()
        {
            if (!_isLogging) return;
            Console.WriteLine($"------[INFO] Logging stopped: {DateTime.Now} ------");
            _logWriter?.Close();
            _logWriter?.Dispose();
            _logWriter = null;
            Console.SetOut(_origConsoleOutput);
            _isLogging = false;
        }

        public static void OnProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("OnProcessExit triggered. Performing final cleanup.");
            PerformShutdownTasks();
            _notifyIcon?.Dispose();
            StopLogging();
        }
    }
    
    public class MultiWriter : TextWriter
    {
        private readonly TextWriter _consoleOut;
        private readonly TextWriter _logWriter;

        public MultiWriter(TextWriter consoleOut, TextWriter logWriter)
        {
            _consoleOut = consoleOut;
            _logWriter = logWriter;
        }

        public override void Write(char value)
        {
            _consoleOut.Write(value);
            _logWriter.Write(value);
        }

        public override void Write(string? value)
        {
            _consoleOut.Write(value);
            _logWriter.Write(value);
        }

        public override Encoding Encoding => _consoleOut.Encoding;
    }
    
    public static class CommandLineArgs
    {
        private static string[] _args;

        public static void SetArgs(string[] args)
        {
            _args = args;
        }
        public static string[] GetArgs()
        {
            return _args;
        }
    }

    
}

