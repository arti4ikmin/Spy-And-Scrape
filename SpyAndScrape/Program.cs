// MIT License
//
// Copyright (c) 2024-2025 Arti
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
        private static DiscordMsgs? _dcMsgs;
        private static HttpClient _httpClient;
        private static NotifierBot _bot;
        private static BotCmds _botCmds;
        private static StreamWriter? _logWriter;
        private static TextWriter _origConsoleOutput;
        private static bool _isLogging = false;
        private static NotifyIcon? _notifyIcon;

        private static bool _isFirstEverRun = false;

        private static string PromptInput(string title, string prompt, bool isPass = false)
        {
            // [STAThread] is critical
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                string? result = null;
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
                return result ?? string.Empty;
            }

            Application.EnableVisualStyles();
            using (var dialog = new InputDialog(title, prompt, isPass))
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    return dialog.InputValue ?? string.Empty;
                }
                return string.Empty;
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
                // PerformShutdownTasks();
                // _notifyIcon?.Dispose();
                // StopLogging();
                // Environment.Exit(1);
            };

            try
            {

                if (args.Length > 0 && args[0] == "delay")
                {
                    Console.WriteLine("THE PROGRAM WAS STARTED WITH DELAY ARGUMENT, WAITING 1.5 SECONDS");
                    await Task.Delay(1500);
                }
                if (args.Length == 0)
                {
                    Console.WriteLine("Program was init without args");
                }
                
                StartLogging();
                Console.WriteLine("SPY AND SCRAPE: starting...");
                
                _httpClient = new HttpClient();
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                await JReader.GetStartingJsonAsync();
                _isFirstEverRun = JReader.IsNewCfgJustCreated;

                Thread.Sleep(100);

                if (string.IsNullOrEmpty(JReader.CurrentConfig.generalBotToken))
                {
                    Console.WriteLine("CRITICAL: No bot token provided");
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
                    await JReader.GetStartingJsonAsync(); // re read in case
                }

                _bot = new NotifierBot();
                _botCmds = new BotCmds(_bot.Client, _bot);
                _bot.AssignBotCommands(_botCmds);


                if (JReader.CurrentConfig.generalBotSetupChannelId == 0 && _isFirstEverRun)
                {
                    Console.WriteLine("CRITICAL: No setup channel ID provided. Prompting user.");
                    var tmpval = PromptInput("Setup Channel ID Required", "CRITICAL: Please provide the starting channel ID for the bot (e.g., for setup messages):");

                    if (string.IsNullOrWhiteSpace(tmpval) || !ulong.TryParse(tmpval, out ulong result) || result == 0)
                    {
                        Console.WriteLine("Invalid or no setup channel ID input was given by the user. Application cannot continue.");
                        MessageBox.Show("A valid starting Bot Setup Channel ID is required. Exiting.", "Critical Configuration Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        PerformShutdownTasks(minimal: true);
                        StopLogging();
                        Environment.Exit(1);
                        return;
                    }

                    JReader.OverwriteConfigValue("generalBotSetupChannelId", result);
                    // if (JReader.CurrentConfig.generalBotLogChannelId == 0) JReader.OverwriteConfigValue("generalBotLogChannelId", result); // now part of interactive setup steps
                    // if (JReader.CurrentConfig.generalBotImportantChannelId == 0) JReader.OverwriteConfigValue("generalBotImportantChannelId", result); 
                    await JReader.GetStartingJsonAsync();
                }


                Task botLifetimeTask; // main runnin task
                
                if (_isFirstEverRun && JReader.CurrentConfig.generalBotSetupChannelId != 0)
                {
                    Console.WriteLine("[Program] New config. Bot will start, then initiate setup.");
                    botLifetimeTask = _bot.StartAsync(JReader.CurrentConfig.generalBotToken);

                    _ = Task.Run(async () => { // bg task for setup
                        await _bot.WaitForReadyAsync();
                        Console.WriteLine("[Program] Bot is ready. Initiating first-run setup via Discord.");
                        await _bot.InitiateFirstRunSetupAsync(JReader.CurrentConfig.generalBotSetupChannelId);
                    });
                }
                else
                {
                    Console.WriteLine("[Program] Existing cfg or initial setup not triggered/required; proceeding with normal startup.");
                    if (_isFirstEverRun)
                    {
                         JReader.IsNewCfgJustCreated = false;
                         Console.WriteLine("[Program] First run flag was true but setup skipped. Ensure config is usable or run /reconfigure.");
                    }
                    _StartTrackersInternal();
                    botLifetimeTask = _bot.StartAsync(JReader.CurrentConfig.generalBotToken);
                }

                await botLifetimeTask; // alive keeper

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL MAIN EX] An err occurred during startup/main execution: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"A critical err occurred: {ex.Message}\nThe app will attempt to continue working. Functional maybe limited, restart if possible", "Critical Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private static void _StartTrackersInternal()
        {
            if (!JReader.IsNewCfgJustCreated)
            {
                var rbTrack = new RobloxTrack();
                if (JReader.CurrentConfig.trackRoblox)
                { _ = Task.Run(() => rbTrack.StartTrackingRoblox(_bot)); }

                _dcMsgs = new DiscordMsgs(_bot);
                if (JReader.CurrentConfig.trackDiscord)
                {
                    _ = Task.Run(() => _dcMsgs.StartTrackingAsync()); 
                    
                    
                    var dcUsrTrack = new DiscordUsrTrack();
                    _ = Task.Run(() => dcUsrTrack.StartTrackingUsr(_bot));
                }
            }
            else
            {
                Console.WriteLine("[Program] Trackers deferred as firstrun setup is still pending completion signal");
            }
        }

        public static void StartTrackersAfterSetup()
        {
            Console.WriteLine("[Program] Firstrun setup done, tracking as configured...");
            _StartTrackersInternal();
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
                } else {
                    Console.WriteLine("Discord message tracking was not started, skipping stop.");
                }


                Console.WriteLine("Shutting down Discord bot...");
                _bot?.ShutdownBot().Wait(TimeSpan.FromSeconds(5));
            }
            Console.WriteLine("Core shutdown tasks completed");
        }


        private static void StartLogging()
        {
            if (_isLogging) return;
            string lFP = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "console_output.txt");

            if (File.Exists(lFP))
            {
                const int maxRetries = 9;
                const int delayOnRetryMilliseconds = 300;

                for (int i = 0; i < maxRetries; ++i)
                {
                    try
                    {
                        File.Delete(lFP);
                        Debug.WriteLine($"[StartLogging] Successfully deleted existing log file: {lFP}");
                        break;
                    }
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"[StartLogging] Attempt {i + 1} to delete log file '{lFP}' failed: {ex.Message}");
                        if (i < maxRetries - 1)
                        {
                            Thread.Sleep(delayOnRetryMilliseconds);
                        }
                        else
                        {
                            Console.Error.WriteLine($"[CRITICAL WARN] StartLogging: Failed to delete existing log file '{lFP}' after {maxRetries} attempts. " + $"The app will attempt to continue, but logging may append to the old file or fail if the file remains locked. Last error: {ex.Message}");
                        }
                    }
                    catch (UnauthorizedAccessException uae)
                    {
                        Console.Error.WriteLine($"[CRITICAL ERROR] StartLogging: No permission to delete log file '{lFP}'. Error: {uae.Message}");
                        break;
                    }
                }
            }

            _origConsoleOutput = Console.Out;
            try
            {
                string? logDirectory = Path.GetDirectoryName(lFP);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                _logWriter = new StreamWriter(lFP, true) { AutoFlush = true }; // 'true' for append, creates if not exists
                Console.SetOut(new MultiWriter(_origConsoleOutput, _logWriter));
                _isLogging = true;
                Console.WriteLine($"------[INFO] Logging started: {DateTime.Now} ------");
            }
            catch (Exception ex)
            {
                Console.SetOut(_origConsoleOutput);
                
                _isLogging = false;
                _logWriter?.Dispose();
                _logWriter = null;

                Debug.WriteLine($"[StartLogging CRITICAL Error] Failed to init StreamWriter for '{lFP}': {ex.Message}");
                (_origConsoleOutput ?? Console.Error).WriteLine($"[CRITICAL ERROR] Failed to start file logging: {ex.Message}. Console output will not be saved to file.");
            }
        }
        private static void StopLogging()
        {
            if (!_isLogging) return;
            Console.WriteLine($"------[INFO] Logging stopped: {DateTime.Now} ------");
            _logWriter?.Flush();
            _logWriter?.Close();
            _logWriter?.Dispose();
            _logWriter = null;

            if (_origConsoleOutput != null)
            {
                Console.SetOut(_origConsoleOutput);
            }
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
        private readonly TextWriter? _logWriter;

        public MultiWriter(TextWriter consoleOut, TextWriter? logWriter)
        {
            _consoleOut = consoleOut;
            _logWriter = logWriter;
        }

        public override void Write(char value)
        {
            _consoleOut.Write(value);
            _logWriter?.Write(value);
        }

        public override void Write(string? value)
        {
            _consoleOut.Write(value);
            _logWriter?.Write(value);
        }

        public override Encoding Encoding => _consoleOut.Encoding;
    }

    public static class CommandLineArgs
    {
        private static string[] _args = Array.Empty<string>();

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