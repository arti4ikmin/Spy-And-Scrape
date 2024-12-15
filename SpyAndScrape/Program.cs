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


using System.Text;
using SpyAndScrape.config;
using SpyAndScrape.Trackers;

namespace SpyAndScrape
{
    class Program
    {
        
        private static DiscordMsgs _dcMsgs;
        
        private static HttpClient _httpClient;

        private static NotifierBot _bot;
        
        // decided to make some logging to a file cuz why not:
        private static StreamWriter _logWriter;
        private static TextWriter _origConsoleOutput;
        private static bool _isLogging = false;
        
        static async Task Main(string[] args)
        {
            CommandLineArgs.SetArgs(args);
            Console.WriteLine("WELCOME TO SPY AND SCRAPE. CONSOLE DISPLAYS A LOT OF SHITPOSTS FOR DEBUG. \n \n");
            
            // handlers for ex so the program isnt getting fucked up or smth
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine($"[Unobserved Task ex] An err was caught, Restart the programm, if continues to presist, please report: {e.Exception.Message}");
                e.SetObserved(); // make the ex as observed -> no instant termination (:skull:)
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                // 
                var ex = (Exception)e.ExceptionObject;
                Console.WriteLine($"[UNHANDLED ex] An error was caught, Restart the programm, if continues to presist, please report: {ex.Message}");
            };

            try {
                
                StartLogging();
                
                if (args.Length > 0 && args[0] == "delay")
                {
                    Console.WriteLine("THE PROGRAMM WAS STARTED WITH DELAY, RESTART; WAITING 2.5 SECONDS");
                    Task.Delay(2500).Wait();
                    Console.WriteLine("");
                }
                if (args.Length == 0)
                {
                    Console.WriteLine("Program was init without args");
                }
                
                _httpClient = new HttpClient();
                
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                Console.CancelKeyPress += OnCancelKeyPress;
                
                await JReader.GetStartingJsonAsync();
                Checks:
                Thread.Sleep(100);
                if (string.IsNullOrEmpty(JReader.CurrentConfig.generalBotToken))
                {
                    Console.WriteLine("\n \nCRITICAL: No bot token provided, please provide bot token: ");
                    var tmpval = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(tmpval)) 
                    {
                        Console.WriteLine("No input was given... Please provide it next time.");
                        goto Checks;
                    }

                    JReader.OverwriteConfigValue("generalBotToken", tmpval);
                }

                if (JReader.CurrentConfig.generalBotSetupChannelId == 0)
                {
                    Console.WriteLine("\n \nCRITICAL: Please provide the starting channel id for the bot: ");
                    var tmpval = Console.ReadLine();

                    if (ulong.TryParse(tmpval, out ulong result))
                    {
                        JReader.OverwriteConfigValue("generalBotSetupChannelId", result);
                        JReader.OverwriteConfigValue("generalBotLogChannelId", result);
                        JReader.OverwriteConfigValue("generalBotImportantChannelId", result);

                    }
                    else
                    {
                        Console.WriteLine("Invalid input or no input provided...");
                        goto Checks;
                    }
                }

                _bot = new NotifierBot();
                
                var rbTrack = new RobloxTrack();
                rbTrack.StartTrackingRoblox(_bot);
                
                if(JReader.CurrentConfig.trackDiscord == true) {
                    _dcMsgs = new DiscordMsgs(_bot);
                    _dcMsgs.StartPyWss();
                }
                

                
                await _bot.StartAsync(JReader.CurrentConfig.generalBotToken);
            
            }
            finally
            {
                
                Console.WriteLine("trying to close wss server...");
                _httpClient.Dispose();
                _dcMsgs?.StopPyWss();
                _bot.ShutdownBot();
                StopLogging();
                
                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();

            }
        }
        
        // for console output
        private static void StartLogging()
        {
            if (_isLogging) return;

            string lFP = "console_output.txt";
            
            if (File.Exists(lFP))
            {
                File.Delete(lFP);
                Console.WriteLine("[INFO] Old log file deleted.");
            }
            
            _origConsoleOutput = Console.Out;

            // StreamWriter very interresting thing
            _logWriter = new StreamWriter(lFP, true);
            _logWriter.AutoFlush = true;

            Console.SetOut(new MultiWriter(_origConsoleOutput, _logWriter));

            _isLogging = true;
            Console.WriteLine("------[INFO] logging started ------");
        }
        private static void StopLogging()
        {
            if (!_isLogging) return;

            Console.WriteLine("------[INFO] logging stopped ------");
            
            _logWriter?.Close();
            Console.SetOut(_origConsoleOutput);
            _isLogging = false;
        }
        

        public static void OnProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("OnProcessExit");
            _httpClient?.Dispose();
            _bot?.ShutdownBot();
            _dcMsgs?.StopPyWss();
            StopLogging();
        }

        public static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("OnCancelKeyPress");
            _dcMsgs?.StopPyWss();
            _bot?.ShutdownBot();
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                StopLogging();
                Environment.Exit(0);
                // real?
            });
            _httpClient?.Dispose();
            StopLogging();
            e.Cancel = true;
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

