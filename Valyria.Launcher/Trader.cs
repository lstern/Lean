using QuantConnect;
using QuantConnect.Configuration;
using QuantConnect.Lean.Engine;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Util;
using System;
using System.Threading;

namespace Valyria.Launcher
{
    public class Job
    {
        public AlgorithmNodePacket Packet { get; set; }
        public string AssemblyPath { get; set; }
    }

    public class Trader
    {
        private LeanEngineSystemHandlers leanEngineSystemHandlers;
        private LeanEngineAlgorithmHandlers leanEngineAlgorithmHandlers;

        private const string _collapseMessage = "Unhandled exception breaking past controls and causing collapse of algorithm node. This is likely a memory leak of an external dependency or the underlying OS terminating the LEAN engine.";

        public void Initialize(string[] args)
        {
            // expect first argument to be config file name
            if (args.Length > 0)
            {
                Config.MergeCommandLineArgumentsWithConfiguration(LeanArgumentParser.ParseArguments(args));
            }

            Log.DebuggingEnabled = Config.GetBool("debug-mode");
            Log.LogHandler = Composer.Instance.GetExportedValueByTypeName<ILogHandler>(Config.Get("log-handler", "CompositeLogHandler"));

            //Name thread for the profiler:
            Thread.CurrentThread.Name = "Algorithm Analysis Thread";
            Log.Trace($"Engine.Main(): LEAN ALGORITHMIC TRADING ENGINE v{Globals.Version} (" + (Environment.Is64BitProcess ? "64" : "32") + "bit)");
            Log.Trace($"Engine.Main(): Started {DateTime.Now.ToShortTimeString()}");

            //Import external libraries specific to physical server location (cloud/local)
            leanEngineSystemHandlers = LeanEngineSystemHandlers.FromConfiguration(Composer.Instance);
            leanEngineAlgorithmHandlers = LeanEngineAlgorithmHandlers.FromConfiguration(Composer.Instance);

            //Setup packeting, queue and controls system: These don't do much locally.
            leanEngineSystemHandlers.Initialize();
        }

        public Job LoadJob()
        {
            //-> Pull job from QuantConnect job queue, or, pull local build:
            var job = leanEngineSystemHandlers.JobQueue.NextJob(out string assemblyPath);

            if (job == null)
            {
                throw new Exception("Engine.Main(): Job was null.");
            }

            // if the job version doesn't match this instance version then we can't process it
            // we also don't want to reprocess redelivered jobs
            if (VersionHelper.IsNotEqualVersion(job.Version) || job.Redelivered)
            {
                Log.Error("Engine.Run(): Job Version: " + job.Version + "  Deployed Version: " + Globals.Version + " Redelivered: " + job.Redelivered);
                //Tiny chance there was an uncontrolled collapse of a server, resulting in an old user task circulating.
                //In this event kill the old algorithm and leave a message so the user can later review.
                leanEngineSystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, AlgorithmStatus.RuntimeError, _collapseMessage);
                leanEngineSystemHandlers.Notify.SetAuthentication(job);
                leanEngineSystemHandlers.Notify.Send(new RuntimeErrorPacket(job.UserId, job.AlgorithmId, _collapseMessage));
                leanEngineSystemHandlers.JobQueue.AcknowledgeJob(job);
                return null;
            }

            return new Job
            {
                Packet = job,
                AssemblyPath = assemblyPath
            };
        }

        public void CleanJob(Job job)
        {
            //Delete the message from the job queue:
            leanEngineSystemHandlers.JobQueue.AcknowledgeJob(job.Packet);
            Log.Trace("Engine.Main(): Packet removed from queue: " + job.Packet.AlgorithmId);

            // clean up resources
            leanEngineSystemHandlers.Dispose();
            leanEngineAlgorithmHandlers.Dispose();
            Log.LogHandler.Dispose();

            Log.Trace("Program.Main(): Exiting Lean...");

            Environment.Exit(0);
        }

        public void RunJob(Job job)
        {
            var liveMode = Config.GetBool("live-mode");
            var algorithmManager = new AlgorithmManager(liveMode);

            leanEngineSystemHandlers.LeanManager.Initialize(leanEngineSystemHandlers, leanEngineAlgorithmHandlers, job.Packet, algorithmManager);

            var engine = new Engine(leanEngineSystemHandlers, leanEngineAlgorithmHandlers, liveMode);
            engine.Run(job.Packet, algorithmManager, job.AssemblyPath);
        }
    }
}
