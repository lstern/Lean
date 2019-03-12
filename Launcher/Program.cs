/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using System;
using System.Diagnostics;
using System.Threading;
using QuantConnect.Configuration;
using QuantConnect.Lean.Engine;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Util;

namespace QuantConnect.Lean.Launcher
{
    public class Program
    {
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (sender, e) =>
            {
                if (e.LoadedAssembly.FullName.ToLower().Contains("python"))
                {
                    Log.Trace($"Python for .NET Assembly: {e.LoadedAssembly.GetName()}");
                }
            };
        }

        static void Main(string[] args)
        {
            ParseArguments(args);
            InitLogger();
            PrepareEnvironment();
            ExecutePendingJobs();
        }

        private static void ExecutePendingJobs()
        {
            var liveMode = Config.GetBool("live-mode");

            using (var engine = new Engine.Engine(liveMode))
            {
                var job = engine.NextJob();

                var isValidJob = engine.ValidateJob(job);
                if (isValidJob)
                {
                    try
                    {
                        var algorithmManager = new AlgorithmManager(liveMode);
                        engine.Run(job, algorithmManager);
                    }
                    finally
                    {
                        //Delete the message from the job queue:
                        engine.AcknowledgeJob(job);

                        Log.Trace("Engine.Main(): Packet removed from queue: " + job.AlgorithmId);
                        Log.LogHandler.Dispose();
                        Log.Trace("Program.Main(): Exiting Lean...");

                        Environment.Exit(0);
                    }
                }
            }
        }

        private static void ParseArguments(string[] args)
        {
            // expect first argument to be config file name
            if (args.Length > 0)
            {
                Config.MergeCommandLineArgumentsWithConfiguration(LeanArgumentParser.ParseArguments(args));
            }
        }

        private static void InitLogger()
        {
            Log.DebuggingEnabled = Config.GetBool("debug-mode");
            Log.LogHandler = Composer.Instance.GetExportedValueByTypeName<ILogHandler>(Config.Get("log-handler", "CompositeLogHandler"));
            Log.Trace($"Engine.Main(): LEAN ALGORITHMIC TRADING ENGINE v{Globals.Version}");
            Log.Trace($"Engine.Main(): Started {DateTime.Now.ToShortTimeString()}");
        }

        private static void PrepareEnvironment()
        {
            Thread.CurrentThread.Name = "Algorithm Analysis Thread";

            if (OS.IsWindows)
            {
                Console.OutputEncoding = System.Text.Encoding.Unicode;
            }

            var environment = Config.Get("environment");
            if (environment.EndsWith("-desktop"))
            {
                var info = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    FileName = Config.Get("desktop-exe"),
                    Arguments = Config.Get("desktop-http-port")
                };

                Process.Start(info);
            }
        }
    }
}
