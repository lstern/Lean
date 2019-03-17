using QuantConnect;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Exceptions;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Lean.Engine.Setup;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Statistics;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Valyria.Launcher
{
    public class Job
    {
        public AlgorithmNodePacket Packet { get; set; }
        public string AssemblyPath { get; set; }
        public bool LiveMode { get; set; }
    }

    public class Trader
    {
        private LeanEngineSystemHandlers leanEngineSystemHandlers;
        private LeanEngineAlgorithmHandlers leanEngineAlgorithmHandlers;

        private const string _collapseMessage = "Unhandled exception breaking past controls and causing collapse of algorithm node. This is likely a memory leak of an external dependency or the underlying OS terminating the LEAN engine.";
        private readonly StackExceptionInterpreter _exceptionInterpreter = StackExceptionInterpreter.CreateFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

        public Trader(string[] args = null)
        {
            if (args != null)
            {
                this.Initialize(args);
            }
        }

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
                AssemblyPath = assemblyPath,
                LiveMode = Config.GetBool("live-mode"),
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

        public void RunJob(Job job, IAlgorithm algorithm = null)
        {
            AlgorithmNodePacket packet = job.Packet;
            string assemblyPath = job.AssemblyPath;
            bool liveMode = job.LiveMode;

            var algorithmManager = new AlgorithmManager(liveMode);

            try
            {
                //Reset thread holders.
                var initializeComplete = false;
                Thread threadTransactions = null;
                Thread threadResults = null;
                Thread threadRealTime = null;
                Thread threadAlphas = null;

                //-> Initialize messaging system
                leanEngineSystemHandlers.Notify.SetAuthentication(packet);

                //-> Set the result handler type for this algorithm job, and launch the associated result thread.
                leanEngineAlgorithmHandlers.Results.Initialize(packet, leanEngineSystemHandlers.Notify, leanEngineSystemHandlers.Api, leanEngineAlgorithmHandlers.Setup, leanEngineAlgorithmHandlers.Transactions);

                threadResults = new Thread(leanEngineAlgorithmHandlers.Results.Run, 0) { IsBackground = true, Name = "Result Thread" };
                threadResults.Start();

                IBrokerage brokerage = null;
                DataManager dataManager = null;
                var synchronizer = new Synchronizer();
                try
                {
                    if (algorithm == null)
                    {
                        // Save algorithm to cache, load algorithm instance:
                        algorithm = leanEngineAlgorithmHandlers.Setup.CreateAlgorithmInstance(packet, assemblyPath);
                    }

                    // Set algorithm in ILeanManager
                    leanEngineSystemHandlers.LeanManager.SetAlgorithm(algorithm);

                    // initialize the alphas handler with the algorithm instance
                    leanEngineAlgorithmHandlers.Alphas.Initialize(packet, algorithm, leanEngineSystemHandlers.Notify, leanEngineSystemHandlers.Api);

                    // Initialize the brokerage
                    brokerage = leanEngineAlgorithmHandlers.Setup.CreateBrokerage(packet, algorithm, out IBrokerageFactory factory);

                    var marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
                    var symbolPropertiesDatabase = SymbolPropertiesDatabase.FromDataFolder();

                    var securityService = new SecurityService(algorithm.Portfolio.CashBook,
                        marketHoursDatabase,
                        symbolPropertiesDatabase,
                        algorithm);

                    algorithm.Securities.SetSecurityService(securityService);

                    dataManager = new DataManager(leanEngineAlgorithmHandlers.DataFeed,
                        new UniverseSelection(
                            algorithm,
                            securityService),
                        algorithm,
                        algorithm.TimeKeeper,
                        marketHoursDatabase);

                    leanEngineAlgorithmHandlers.Results.SetDataManager(dataManager);
                    algorithm.SubscriptionManager.SetDataManager(dataManager);

                    synchronizer.Initialize(
                        algorithm,
                        dataManager,
                        liveMode);

                    // Initialize the data feed before we initialize so he can intercept added securities/universes via events
                    leanEngineAlgorithmHandlers.DataFeed.Initialize(
                        algorithm,
                        packet,
                        leanEngineAlgorithmHandlers.Results,
                        leanEngineAlgorithmHandlers.MapFileProvider,
                        leanEngineAlgorithmHandlers.FactorFileProvider,
                        leanEngineAlgorithmHandlers.DataProvider,
                        dataManager,
                        synchronizer);

                    // set the order processor on the transaction manager (needs to be done before initializing BrokerageHistoryProvider)
                    algorithm.Transactions.SetOrderProcessor(leanEngineAlgorithmHandlers.Transactions);

                    // set the history provider before setting up the algorithm
                    var historyProvider = GetHistoryProvider(packet.HistoryProvider);
                    if (historyProvider is BrokerageHistoryProvider)
                    {
                        (historyProvider as BrokerageHistoryProvider).SetBrokerage(brokerage);
                    }

                    var historyDataCacheProvider = new ZipDataCacheProvider(leanEngineAlgorithmHandlers.DataProvider);
                    historyProvider.Initialize(
                        new HistoryProviderInitializeParameters(
                            packet,
                            leanEngineSystemHandlers.Api,
                            leanEngineAlgorithmHandlers.DataProvider,
                            historyDataCacheProvider,
                            leanEngineAlgorithmHandlers.MapFileProvider,
                            leanEngineAlgorithmHandlers.FactorFileProvider,
                            progress =>
                            {
                                // send progress updates to the result handler only during initialization
                                if (!algorithm.GetLocked() || algorithm.IsWarmingUp)
                                {
                                    leanEngineAlgorithmHandlers.Results.SendStatusUpdate(AlgorithmStatus.History,
                                        string.Format("Processing history {0}%...", progress));
                                }
                            }
                        )
                    );

                    historyProvider.InvalidConfigurationDetected += (sender, args) => { leanEngineAlgorithmHandlers.Results.ErrorMessage(args.Message); };
                    historyProvider.NumericalPrecisionLimited += (sender, args) => { leanEngineAlgorithmHandlers.Results.DebugMessage(args.Message); };
                    historyProvider.DownloadFailed += (sender, args) => { leanEngineAlgorithmHandlers.Results.ErrorMessage(args.Message, args.StackTrace); };
                    historyProvider.ReaderErrorDetected += (sender, args) => { leanEngineAlgorithmHandlers.Results.RuntimeError(args.Message, args.StackTrace); };

                    algorithm.HistoryProvider = historyProvider;

                    // initialize the default brokerage message handler
                    algorithm.BrokerageMessageHandler = factory.CreateBrokerageMessageHandler(algorithm, packet, leanEngineSystemHandlers.Api);

                    //Initialize the internal state of algorithm and job: executes the algorithm.Initialize() method.
                    initializeComplete = leanEngineAlgorithmHandlers.Setup.Setup(new SetupHandlerParameters(dataManager.UniverseSelection, algorithm, brokerage, packet, leanEngineAlgorithmHandlers.Results, leanEngineAlgorithmHandlers.Transactions, leanEngineAlgorithmHandlers.RealTime));

                    // set this again now that we've actually added securities
                    leanEngineAlgorithmHandlers.Results.SetAlgorithm(algorithm);

                    // alpha handler needs start/end dates to determine sample step sizes
                    leanEngineAlgorithmHandlers.Alphas.OnAfterAlgorithmInitialized(algorithm);

                    //If there are any reasons it failed, pass these back to the IDE.
                    if (!initializeComplete || algorithm.ErrorMessages.Count > 0 || leanEngineAlgorithmHandlers.Setup.Errors.Count > 0)
                    {
                        initializeComplete = false;
                        //Get all the error messages: internal in algorithm and external in setup handler.
                        var errorMessage = String.Join(",", algorithm.ErrorMessages);
                        errorMessage += String.Join(",", leanEngineAlgorithmHandlers.Setup.Errors.Select(e =>
                        {
                            var message = e.Message;
                            if (e.InnerException != null)
                            {
                                var err = _exceptionInterpreter.Interpret(e.InnerException, _exceptionInterpreter);
                                message += _exceptionInterpreter.GetExceptionMessageHeader(err);
                            }
                            return message;
                        }));
                        Log.Error("Engine.Run(): " + errorMessage);
                        leanEngineAlgorithmHandlers.Results.RuntimeError(errorMessage);
                        leanEngineSystemHandlers.Api.SetAlgorithmStatus(packet.AlgorithmId, AlgorithmStatus.RuntimeError, errorMessage);
                    }
                }
                catch (Exception err)
                {
                    Log.Error(err);
                    var runtimeMessage = "Algorithm.Initialize() Error: " + err.Message + " Stack Trace: " + err;
                    leanEngineAlgorithmHandlers.Results.RuntimeError(runtimeMessage, err.ToString());
                    leanEngineSystemHandlers.Api.SetAlgorithmStatus(packet.AlgorithmId, AlgorithmStatus.RuntimeError, runtimeMessage);
                }


                // log the job endpoints
                Log.Trace("JOB HANDLERS: ");
                Log.Trace("         DataFeed:     " + leanEngineAlgorithmHandlers.DataFeed.GetType().FullName);
                Log.Trace("         Setup:        " + leanEngineAlgorithmHandlers.Setup.GetType().FullName);
                Log.Trace("         RealTime:     " + leanEngineAlgorithmHandlers.RealTime.GetType().FullName);
                Log.Trace("         Results:      " + leanEngineAlgorithmHandlers.Results.GetType().FullName);
                Log.Trace("         Transactions: " + leanEngineAlgorithmHandlers.Transactions.GetType().FullName);
                Log.Trace("         Alpha:        " + leanEngineAlgorithmHandlers.Alphas.GetType().FullName);
                if (algorithm?.HistoryProvider != null)
                {
                    Log.Trace("         History Provider:     " + algorithm.HistoryProvider.GetType().FullName);
                }
                if (packet is LiveNodePacket) Log.Trace("         Brokerage:      " + brokerage?.GetType().FullName);

                //-> Using the job + initialization: load the designated handlers:
                if (initializeComplete)
                {
                    // notify the LEAN manager that the algorithm is initialized and starting
                    leanEngineSystemHandlers.LeanManager.OnAlgorithmStart();

                    //-> Reset the backtest stopwatch; we're now running the algorithm.
                    var startTime = DateTime.Now;

                    //Set algorithm as locked; set it to live mode if we're trading live, and set it to locked for no further updates.
                    algorithm.SetAlgorithmId(packet.AlgorithmId);
                    algorithm.SetLocked();

                    //Load the associated handlers for transaction and realtime events:
                    leanEngineAlgorithmHandlers.Transactions.Initialize(algorithm, brokerage, leanEngineAlgorithmHandlers.Results);
                    leanEngineAlgorithmHandlers.RealTime.Setup(algorithm, packet, leanEngineAlgorithmHandlers.Results, leanEngineSystemHandlers.Api);

                    // wire up the brokerage message handler
                    brokerage.Message += (sender, message) =>
                    {
                        algorithm.BrokerageMessageHandler.Handle(message);

                        // fire brokerage message events
                        algorithm.OnBrokerageMessage(message);
                        switch (message.Type)
                        {
                            case BrokerageMessageType.Disconnect:
                                algorithm.OnBrokerageDisconnect();
                                break;
                            case BrokerageMessageType.Reconnect:
                                algorithm.OnBrokerageReconnect();
                                break;
                        }
                    };

                    //Send status to user the algorithm is now executing.
                    leanEngineAlgorithmHandlers.Results.SendStatusUpdate(AlgorithmStatus.Running);

                    //Launch the data, transaction and realtime handlers into dedicated threads
                    threadTransactions = new Thread(leanEngineAlgorithmHandlers.Transactions.Run) { IsBackground = true, Name = "Transaction Thread" };
                    threadRealTime = new Thread(leanEngineAlgorithmHandlers.RealTime.Run) { IsBackground = true, Name = "RealTime Thread" };
                    threadAlphas = new Thread(() => leanEngineAlgorithmHandlers.Alphas.Run()) { IsBackground = true, Name = "Alpha Thread" };

                    //Launch the data feed, result sending, and transaction models/handlers in separate threads.
                    threadTransactions.Start(); // Transaction modeller scanning new order requests
                    threadRealTime.Start(); // RealTime scan time for time based events:
                    threadAlphas.Start(); // Alpha thread for processing algorithm alpha insights

                    // Result manager scanning message queue: (started earlier)
                    leanEngineAlgorithmHandlers.Results.DebugMessage(string.Format("Launching analysis for {0} with LEAN Engine v{1}", packet.AlgorithmId, Globals.Version));

                    try
                    {
                        //Create a new engine isolator class
                        var isolator = new Isolator();

                        // Execute the Algorithm Code:
                        var complete = isolator.ExecuteWithTimeLimit(leanEngineAlgorithmHandlers.Setup.MaximumRuntime, algorithmManager.TimeLoopWithinLimits, () =>
                        {
                            try
                            {
                                //Run Algorithm Job:
                                // -> Using this Data Feed,
                                // -> Send Orders to this TransactionHandler,
                                // -> Send Results to ResultHandler.
                                algorithmManager.Run(packet, algorithm, synchronizer, leanEngineAlgorithmHandlers.Transactions, leanEngineAlgorithmHandlers.Results, leanEngineAlgorithmHandlers.RealTime, leanEngineSystemHandlers.LeanManager, leanEngineAlgorithmHandlers.Alphas, isolator.CancellationToken);
                            }
                            catch (Exception err)
                            {
                                //Debugging at this level is difficult, stack trace needed.
                                Log.Error(err);
                                algorithm.RunTimeError = err;
                                algorithmManager.SetStatus(AlgorithmStatus.RuntimeError);
                                return;
                            }

                            Log.Trace("Engine.Run(): Exiting Algorithm Manager");
                        }, packet.Controls.RamAllocation);

                        if (!complete)
                        {
                            Log.Error("Engine.Main(): Failed to complete in time: " + leanEngineAlgorithmHandlers.Setup.MaximumRuntime.ToString("F"));
                            throw new Exception("Failed to complete algorithm within " + leanEngineAlgorithmHandlers.Setup.MaximumRuntime.ToString("F")
                                + " seconds. Please make it run faster.");
                        }

                        // Algorithm runtime error:
                        if (algorithm.RunTimeError != null)
                        {
                            HandleAlgorithmError(packet, algorithm.RunTimeError);
                        }
                    }
                    catch (Exception err)
                    {
                        //Error running the user algorithm: purge datafeed, send error messages, set algorithm status to failed.
                        HandleAlgorithmError(packet, err);
                    }

                    // notify the LEAN manager that the algorithm has finished
                    leanEngineSystemHandlers.LeanManager.OnAlgorithmEnd();

                    try
                    {
                        var trades = algorithm.TradeBuilder.ClosedTrades;
                        var charts = new Dictionary<string, Chart>(leanEngineAlgorithmHandlers.Results.Charts);
                        var orders = new Dictionary<int, QuantConnect.Orders.Order>(leanEngineAlgorithmHandlers.Transactions.Orders);
                        var holdings = new Dictionary<string, Holding>();
                        var banner = new Dictionary<string, string>();
                        var statisticsResults = new StatisticsResults();

                        var csvTransactionsFileName = Config.Get("transaction-log");
                        if (!string.IsNullOrEmpty(csvTransactionsFileName))
                        {
                            SaveListOfTrades(leanEngineAlgorithmHandlers.Transactions, csvTransactionsFileName);
                        }

                        try
                        {
                            //Generates error when things don't exist (no charting logged, runtime errors in main algo execution)
                            const string strategyEquityKey = "Strategy Equity";
                            const string equityKey = "Equity";
                            const string dailyPerformanceKey = "Daily Performance";
                            const string benchmarkKey = "Benchmark";

                            // make sure we've taken samples for these series before just blindly requesting them
                            if (charts.ContainsKey(strategyEquityKey) &&
                                charts[strategyEquityKey].Series.ContainsKey(equityKey) &&
                                charts[strategyEquityKey].Series.ContainsKey(dailyPerformanceKey) &&
                                charts.ContainsKey(benchmarkKey) &&
                                charts[benchmarkKey].Series.ContainsKey(benchmarkKey)
                            )
                            {
                                var equity = charts[strategyEquityKey].Series[equityKey].Values;
                                var performance = charts[strategyEquityKey].Series[dailyPerformanceKey].Values;
                                var profitLoss = new SortedDictionary<DateTime, decimal>(algorithm.Transactions.TransactionRecord);
                                var totalTransactions = algorithm.Transactions.GetOrders(x => x.Status.IsFill()).Count();
                                var benchmark = charts[benchmarkKey].Series[benchmarkKey].Values;

                                statisticsResults = StatisticsBuilder.Generate(trades, profitLoss, equity, performance, benchmark,
                                    leanEngineAlgorithmHandlers.Setup.StartingPortfolioValue, algorithm.Portfolio.TotalFees, totalTransactions);

                                //Some users have $0 in their brokerage account / starting cash of $0. Prevent divide by zero errors
                                var netReturn = leanEngineAlgorithmHandlers.Setup.StartingPortfolioValue > 0 ?
                                                (algorithm.Portfolio.TotalPortfolioValue - leanEngineAlgorithmHandlers.Setup.StartingPortfolioValue) / leanEngineAlgorithmHandlers.Setup.StartingPortfolioValue
                                                : 0;

                                //Add other fixed parameters.
                                banner.Add("Unrealized", "$" + algorithm.Portfolio.TotalUnrealizedProfit.ToString("N2"));
                                banner.Add("Fees", "-$" + algorithm.Portfolio.TotalFees.ToString("N2"));
                                banner.Add("Net Profit", "$" + algorithm.Portfolio.TotalProfit.ToString("N2"));
                                banner.Add("Return", netReturn.ToString("P"));
                                banner.Add("Equity", "$" + algorithm.Portfolio.TotalPortfolioValue.ToString("N2"));
                            }
                        }
                        catch (Exception err)
                        {
                            Log.Error(err, "Error generating statistics packet");
                        }

                        //Diagnostics Completed, Send Result Packet:
                        var totalSeconds = (DateTime.Now - startTime).TotalSeconds;
                        var dataPoints = algorithmManager.DataPoints + algorithm.HistoryProvider.DataPointCount;

                        if (!liveMode)
                        {
                            var kps = dataPoints / (double)1000 / totalSeconds;
                            leanEngineAlgorithmHandlers.Results.DebugMessage($"Algorithm Id:({packet.AlgorithmId}) completed in {totalSeconds:F2} seconds at {kps:F0}k data points per second. Processing total of {dataPoints:N0} data points.");
                        }

                        leanEngineAlgorithmHandlers.Results.SendFinalResult(packet, orders, algorithm.Transactions.TransactionRecord, holdings, algorithm.Portfolio.CashBook, statisticsResults, banner);
                    }
                    catch (Exception err)
                    {
                        Log.Error(err, "Error sending analysis results");
                    }

                    //Before we return, send terminate commands to close up the threads
                    leanEngineAlgorithmHandlers.Transactions.Exit();
                    leanEngineAlgorithmHandlers.DataFeed.Exit();
                    leanEngineAlgorithmHandlers.RealTime.Exit();
                    leanEngineAlgorithmHandlers.Alphas.Exit();
                    dataManager?.RemoveAllSubscriptions();
                }

                //Close result handler:
                leanEngineAlgorithmHandlers.Results.Exit();

                //Wait for the threads to complete:
                var ts = Stopwatch.StartNew();
                while ((leanEngineAlgorithmHandlers.Results.IsActive
                    || (leanEngineAlgorithmHandlers.Transactions != null && leanEngineAlgorithmHandlers.Transactions.IsActive)
                    || (leanEngineAlgorithmHandlers.DataFeed != null && leanEngineAlgorithmHandlers.DataFeed.IsActive)
                    || (leanEngineAlgorithmHandlers.RealTime != null && leanEngineAlgorithmHandlers.RealTime.IsActive)
                    || (leanEngineAlgorithmHandlers.Alphas != null && leanEngineAlgorithmHandlers.Alphas.IsActive))
                    && ts.ElapsedMilliseconds < 30 * 1000)
                {
                    Thread.Sleep(100);
                    Log.Trace("Waiting for threads to exit...");
                }

                //Terminate threads still in active state.
                if (threadTransactions != null && threadTransactions.IsAlive) threadTransactions.Abort();
                if (threadResults != null && threadResults.IsAlive) threadResults.Abort();
                if (threadAlphas != null && threadAlphas.IsAlive) threadAlphas.Abort();

                if (brokerage != null)
                {
                    Log.Trace("Engine.Run(): Disconnecting from brokerage...");
                    brokerage.Disconnect();
                    brokerage.Dispose();
                }
                if (leanEngineAlgorithmHandlers.Setup != null)
                {
                    Log.Trace("Engine.Run(): Disposing of setup handler...");
                    leanEngineAlgorithmHandlers.Setup.Dispose();
                }
                Log.Trace("Engine.Main(): Analysis Completed and Results Posted.");
            }
            catch (Exception err)
            {
                Log.Error(err, "Error running algorithm");
            }
            finally
            {
                //No matter what for live mode; make sure we've set algorithm status in the API for "not running" conditions:
                if (liveMode && algorithmManager.State != AlgorithmStatus.Running && algorithmManager.State != AlgorithmStatus.RuntimeError)
                    leanEngineSystemHandlers.Api.SetAlgorithmStatus(packet.AlgorithmId, algorithmManager.State);

                leanEngineAlgorithmHandlers.Results.Exit();
                leanEngineAlgorithmHandlers.DataFeed.Exit();
                leanEngineAlgorithmHandlers.Transactions.Exit();
                leanEngineAlgorithmHandlers.RealTime.Exit();
            }
        }

        /// <summary>
        /// Handle an error in the algorithm.Run method.
        /// </summary>
        /// <param name="job">Job we're processing</param>
        /// <param name="err">Error from algorithm stack</param>
        private void HandleAlgorithmError(AlgorithmNodePacket job, Exception err)
        {
            Log.Error(err, "Breaking out of parent try catch:");
            if (leanEngineAlgorithmHandlers.DataFeed != null) leanEngineAlgorithmHandlers.DataFeed.Exit();
            if (leanEngineAlgorithmHandlers.Results != null)
            {
                // perform exception interpretation
                err = _exceptionInterpreter.Interpret(err, _exceptionInterpreter);

                var message = "Runtime Error: " + _exceptionInterpreter.GetExceptionMessageHeader(err);
                Log.Trace("Engine.Run(): Sending runtime error to user...");
                leanEngineAlgorithmHandlers.Results.LogMessage(message);
                leanEngineAlgorithmHandlers.Results.RuntimeError(message, err.ToString());
                leanEngineSystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, AlgorithmStatus.RuntimeError, message + " Stack Trace: " + err);
            }
        }

        /// <summary>
        /// Load the history provider from the Composer
        /// </summary>
        private IHistoryProvider GetHistoryProvider(string historyProvider)
        {
            if (historyProvider.IsNullOrEmpty())
            {
                historyProvider = Config.Get("history-provider", "SubscriptionDataReaderHistoryProvider");
            }
            return Composer.Instance.GetExportedValueByTypeName<IHistoryProvider>(historyProvider);
        }

        /// <summary>
        /// Save a list of trades to disk for a given path
        /// </summary>
        /// <param name="transactions">Transactions list via an OrderProvider</param>
        /// <param name="csvFileName">File path to create</param>
        private static void SaveListOfTrades(IOrderProvider transactions, string csvFileName)
        {
            var orders = transactions.GetOrders(x => x.Status.IsFill());

            var path = Path.GetDirectoryName(csvFileName);
            if (path != null && !Directory.Exists(path))
                Directory.CreateDirectory(path);

            using (var writer = new StreamWriter(csvFileName))
            {
                foreach (var order in orders)
                {
                    var line = string.Format("{0},{1},{2},{3},{4}",
                        order.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                        order.Symbol.Value,
                        order.Direction,
                        order.Quantity,
                        order.Price);
                    writer.WriteLine(line);
                }
            }
        }
    }
}
