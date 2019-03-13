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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Exceptions;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Lean.Engine.Setup;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Statistics;
using QuantConnect.Util;

namespace QuantConnect.Lean.Engine
{
    /// <summary>
    /// LEAN ALGORITHMIC TRADING ENGINE: ENTRY POINT.
    ///
    /// The engine loads new tasks, create the algorithms and threads, and sends them
    /// to Algorithm Manager to be executed. It is the primary operating loop.
    /// </summary>
    public class Engine : IDisposable
    {
        private readonly bool _liveMode;
        private readonly StackExceptionInterpreter _exceptionInterpreter = StackExceptionInterpreter.CreateFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

        /// <summary>
        /// Gets the configured system handlers for this engine instance
        /// </summary>
        public LeanEngineSystemHandlers SystemHandlers { get; }

        /// <summary>
        /// Gets the configured algorithm handlers for this engine instance
        /// </summary>
        public LeanEngineAlgorithmHandlers AlgorithmHandlers { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Engine"/> class using the specified handlers
        /// </summary>
        /// <param name="liveMode">True when running in live mode, false otherwises</param>
        public Engine(bool liveMode)
        {
            _liveMode = liveMode;
            SystemHandlers = LeanEngineSystemHandlers.FromConfiguration(Composer.Instance);
            AlgorithmHandlers = LeanEngineAlgorithmHandlers.FromConfiguration(Composer.Instance);
        }

        public AlgorithmNodePacket NextJob()
        {
            return SystemHandlers.JobQueue.NextJob();
        }

        public void AcknowledgeJob(AlgorithmNodePacket job)
        {
            SystemHandlers.JobQueue.AcknowledgeJob(job);
        }

        public bool ValidateJob(AlgorithmNodePacket job)
        {
            // if the job version doesn't match this instance version then we can't process it
            // we also don't want to reprocess redelivered jobs
            if (!VersionHelper.IsNotEqualVersion(job.Version) && !job.Redelivered)
            {
                return true;
            }

            var collapseMessage = "Unhandled exception breaking past controls and causing collapse of algorithm node. This is likely a memory leak of an external dependency or the underlying OS terminating the LEAN engine.";

            Log.Error("Engine.Run(): Job Version: " + job.Version + "  Deployed Version: " + Globals.Version + " Redelivered: " + job.Redelivered);
            //Tiny chance there was an uncontrolled collapse of a server, resulting in an old user task circulating.
            //In this event kill the old algorithm and leave a message so the user can later review.
            SystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, AlgorithmStatus.RuntimeError, collapseMessage);
            SystemHandlers.Notify.SetAuthentication(job);
            SystemHandlers.Notify.Send(new RuntimeErrorPacket(job.UserId, job.AlgorithmId, collapseMessage));
            SystemHandlers.JobQueue.AcknowledgeJob(job);

            return false;
        }

        private Thread[] ExecuteAlgorithm(DataManager dataManager, IAlgorithm algorithm, AlgorithmNodePacket job, IBrokerage brokerage, AlgorithmManager algorithmManager, Synchronizer synchronizer)
        {
            // notify the LEAN manager that the algorithm is initialized and starting
            SystemHandlers.LeanManager.OnAlgorithmStart();

            //-> Reset the backtest stopwatch; we're now running the algorithm.
            var startTime = DateTime.Now;

            //Set algorithm as locked; set it to live mode if we're trading live, and set it to locked for no further updates.
            algorithm.SetAlgorithmId(job.AlgorithmId);
            algorithm.SetLocked();

            //Load the associated handlers for transaction and realtime events:
            AlgorithmHandlers.Transactions.Initialize(algorithm, brokerage, AlgorithmHandlers.Results);
            AlgorithmHandlers.RealTime.Setup(algorithm, job, AlgorithmHandlers.Results, SystemHandlers.Api);

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
            AlgorithmHandlers.Results.SendStatusUpdate(AlgorithmStatus.Running);

            //Launch the data, transaction and realtime handlers into dedicated threads
            var threadTransactions = new Thread(AlgorithmHandlers.Transactions.Run) { IsBackground = true, Name = "Transaction Thread" };
            var threadRealTime = new Thread(AlgorithmHandlers.RealTime.Run) { IsBackground = true, Name = "RealTime Thread" };
            var threadAlphas = new Thread(() => AlgorithmHandlers.Alphas.Run()) { IsBackground = true, Name = "Alpha Thread" };

            //Launch the data feed, result sending, and transaction models/handlers in separate threads.
            threadTransactions.Start(); // Transaction modeller scanning new order requests
            threadRealTime.Start(); // RealTime scan time for time based events:
            threadAlphas.Start(); // Alpha thread for processing algorithm alpha insights

            // Result manager scanning message queue: (started earlier)
            AlgorithmHandlers.Results.DebugMessage(string.Format("Launching analysis for {0} with LEAN Engine v{1}", job.AlgorithmId, Globals.Version));

            try
            {
                //Create a new engine isolator class
                var isolator = new Isolator();

                // Execute the Algorithm Code:
                var complete = isolator.ExecuteWithTimeLimit(AlgorithmHandlers.Setup.MaximumRuntime, algorithmManager.TimeLoopWithinLimits, () =>
                {
                    try
                    {
                        //Run Algorithm Job:
                        // -> Using this Data Feed,
                        // -> Send Orders to this TransactionHandler,
                        // -> Send Results to ResultHandler.
                        algorithmManager.Run(job, algorithm, synchronizer, AlgorithmHandlers.Transactions, AlgorithmHandlers.Results, AlgorithmHandlers.RealTime, SystemHandlers.LeanManager, AlgorithmHandlers.Alphas, isolator.CancellationToken);
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
                }, job.Controls.RamAllocation);

                if (!complete)
                {
                    Log.Error("Engine.Main(): Failed to complete in time: " + AlgorithmHandlers.Setup.MaximumRuntime.ToString("F"));
                    throw new Exception("Failed to complete algorithm within " + AlgorithmHandlers.Setup.MaximumRuntime.ToString("F")
                        + " seconds. Please make it run faster.");
                }

                // Algorithm runtime error:
                if (algorithm.RunTimeError != null)
                {
                    HandleAlgorithmError(job, algorithm.RunTimeError);
                }
            }
            catch (Exception err)
            {
                //Error running the user algorithm: purge datafeed, send error messages, set algorithm status to failed.
                HandleAlgorithmError(job, err);
            }

            // notify the LEAN manager that the algorithm has finished
            SystemHandlers.LeanManager.OnAlgorithmEnd();

            try
            {
                var trades = algorithm.TradeBuilder.ClosedTrades;
                var charts = new Dictionary<string, Chart>(AlgorithmHandlers.Results.Charts);
                var orders = new Dictionary<int, Order>(AlgorithmHandlers.Transactions.Orders);
                var holdings = new Dictionary<string, Holding>();
                var banner = new Dictionary<string, string>();
                var statisticsResults = new StatisticsResults();

                var csvTransactionsFileName = Config.Get("transaction-log");
                if (!string.IsNullOrEmpty(csvTransactionsFileName))
                {
                    SaveListOfTrades(AlgorithmHandlers.Transactions, csvTransactionsFileName);
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
                            AlgorithmHandlers.Setup.StartingPortfolioValue, algorithm.Portfolio.TotalFees, totalTransactions);

                        //Some users have $0 in their brokerage account / starting cash of $0. Prevent divide by zero errors
                        var netReturn = AlgorithmHandlers.Setup.StartingPortfolioValue > 0 ?
                                        (algorithm.Portfolio.TotalPortfolioValue - AlgorithmHandlers.Setup.StartingPortfolioValue) / AlgorithmHandlers.Setup.StartingPortfolioValue
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

                if (!_liveMode)
                {
                    var kps = dataPoints / (double)1000 / totalSeconds;
                    AlgorithmHandlers.Results.DebugMessage($"Algorithm Id:({job.AlgorithmId}) completed in {totalSeconds:F2} seconds at {kps:F0}k data points per second. Processing total of {dataPoints:N0} data points.");
                }

                AlgorithmHandlers.Results.SendFinalResult(job, orders, algorithm.Transactions.TransactionRecord, holdings, algorithm.Portfolio.CashBook, statisticsResults, banner);
            }
            catch (Exception err)
            {
                Log.Error(err, "Error sending analysis results");
            }

            //Before we return, send terminate commands to close up the threads
            AlgorithmHandlers.Transactions.Exit();
            AlgorithmHandlers.DataFeed.Exit();
            AlgorithmHandlers.RealTime.Exit();
            AlgorithmHandlers.Alphas.Exit();
            dataManager?.RemoveAllSubscriptions();

            return new Thread[] { threadAlphas, threadRealTime, threadTransactions };
        }

        /// <summary>
        /// Runs a single backtest/live job from the job queue
        /// </summary>
        /// <param name="job">The algorithm job to be processed</param>
        /// <param name="manager"></param>
        public void Run(AlgorithmNodePacket job, AlgorithmManager manager)
        {
            var algorithm = default(IAlgorithm);
            var algorithmManager = manager;

            try
            {
                //Reset thread holders.
                Thread threadResults = null;

                //-> Initialize messaging system
                SystemHandlers.Notify.SetAuthentication(job);

                //-> Set the result handler type for this algorithm job, and launch the associated result thread.
                AlgorithmHandlers.Results.Initialize(job, SystemHandlers.Notify, SystemHandlers.Api, AlgorithmHandlers.Setup, AlgorithmHandlers.Transactions);

                threadResults = new Thread(AlgorithmHandlers.Results.Run, 0) { IsBackground = true, Name = "Result Thread" };
                threadResults.Start();

                IBrokerage brokerage = null;
                DataManager dataManager = null;
                var synchronizer = new Synchronizer();

                var initializeComplete = TryInitializeRun(job, brokerage, dataManager, synchronizer, out algorithm);

                Thread[] threads = new Thread[0];

                //-> Using the job + initialization: load the designated handlers:
                if (initializeComplete)
                {
                   threads = ExecuteAlgorithm(dataManager, algorithm, job, brokerage, algorithmManager, synchronizer);
                }

                //Close result handler:
                AlgorithmHandlers.Results.Exit();

                //Wait for the threads to complete:
                var ts = Stopwatch.StartNew();
                while ((AlgorithmHandlers.Results.IsActive
                    || (AlgorithmHandlers.Transactions != null && AlgorithmHandlers.Transactions.IsActive)
                    || (AlgorithmHandlers.DataFeed != null && AlgorithmHandlers.DataFeed.IsActive)
                    || (AlgorithmHandlers.RealTime != null && AlgorithmHandlers.RealTime.IsActive)
                    || (AlgorithmHandlers.Alphas != null && AlgorithmHandlers.Alphas.IsActive))
                    && ts.ElapsedMilliseconds < 30 * 1000)
                {
                    Thread.Sleep(100);
                    Log.Trace("Waiting for threads to exit...");
                }

                //Terminate threads still in active state.
                foreach (var thread in threads)
                {
                    if (thread != null && thread.IsAlive)
                    {
                        thread.Abort();
                    }
                }

                if (brokerage != null)
                {
                    Log.Trace("Engine.Run(): Disconnecting from brokerage...");
                    brokerage.Disconnect();
                    brokerage.Dispose();
                }
                if (AlgorithmHandlers.Setup != null)
                {
                    Log.Trace("Engine.Run(): Disposing of setup handler...");
                    AlgorithmHandlers.Setup.Dispose();
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
                if (_liveMode && algorithmManager.State != AlgorithmStatus.Running && algorithmManager.State != AlgorithmStatus.RuntimeError)
                    SystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, algorithmManager.State);

                AlgorithmHandlers.Results.Exit();
                AlgorithmHandlers.DataFeed.Exit();
                AlgorithmHandlers.Transactions.Exit();
                AlgorithmHandlers.RealTime.Exit();
            }
        }

        private bool TryInitializeRun(AlgorithmNodePacket job, IBrokerage brokerage, DataManager dataManager, Synchronizer synchronizer, out IAlgorithm algorithm)
        {
            bool initializeComplete = false;
            algorithm = default;

            try
            {
                // Save algorithm to cache, load algorithm instance:
                algorithm = AlgorithmHandlers.Setup.CreateAlgorithmInstance(job, job.AlgorithmPath);

                // Set algorithm in ILeanManager
                SystemHandlers.LeanManager.SetAlgorithm(algorithm);

                // initialize the alphas handler with the algorithm instance
                AlgorithmHandlers.Alphas.Initialize(job, algorithm, SystemHandlers.Notify, SystemHandlers.Api);

                // Initialize the brokerage
                brokerage = AlgorithmHandlers.Setup.CreateBrokerage(job, algorithm, out IBrokerageFactory factory);

                var marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
                var symbolPropertiesDatabase = SymbolPropertiesDatabase.FromDataFolder();

                var securityService = new SecurityService(algorithm.Portfolio.CashBook,
                    marketHoursDatabase,
                    symbolPropertiesDatabase,
                    algorithm);

                algorithm.Securities.SetSecurityService(securityService);

                dataManager = new DataManager(AlgorithmHandlers.DataFeed,
                    new UniverseSelection(
                        algorithm,
                        securityService),
                    algorithm,
                    algorithm.TimeKeeper,
                    marketHoursDatabase);

                AlgorithmHandlers.Results.SetDataManager(dataManager);
                algorithm.SubscriptionManager.SetDataManager(dataManager);

                synchronizer.Initialize(
                    algorithm,
                    dataManager,
                    _liveMode);

                // Initialize the data feed before we initialize so he can intercept added securities/universes via events
                AlgorithmHandlers.DataFeed.Initialize(
                    algorithm,
                    job,
                    AlgorithmHandlers.Results,
                    AlgorithmHandlers.MapFileProvider,
                    AlgorithmHandlers.FactorFileProvider,
                    AlgorithmHandlers.DataProvider,
                    dataManager,
                    synchronizer);

                // set the order processor on the transaction manager (needs to be done before initializing BrokerageHistoryProvider)
                algorithm.Transactions.SetOrderProcessor(AlgorithmHandlers.Transactions);

                // set the history provider before setting up the algorithm
                var historyProvider = GetHistoryProvider(job.HistoryProvider);
                if (historyProvider is BrokerageHistoryProvider)
                {
                    (historyProvider as BrokerageHistoryProvider).SetBrokerage(brokerage);
                }

                var historyDataCacheProvider = new ZipDataCacheProvider(AlgorithmHandlers.DataProvider);
                var algo = algorithm;
                historyProvider.Initialize(
                    new HistoryProviderInitializeParameters(
                        job,
                        SystemHandlers.Api,
                        AlgorithmHandlers.DataProvider,
                        historyDataCacheProvider,
                        AlgorithmHandlers.MapFileProvider,
                        AlgorithmHandlers.FactorFileProvider,
                        progress =>
                        {
                                // send progress updates to the result handler only during initialization
                                if (!algo.GetLocked() || algo.IsWarmingUp)
                            {
                                AlgorithmHandlers.Results.SendStatusUpdate(AlgorithmStatus.History,
                                    string.Format("Processing history {0}%...", progress));
                            }
                        }
                    )
                );

                historyProvider.InvalidConfigurationDetected += (sender, args) => { AlgorithmHandlers.Results.ErrorMessage(args.Message); };
                historyProvider.NumericalPrecisionLimited += (sender, args) => { AlgorithmHandlers.Results.DebugMessage(args.Message); };
                historyProvider.DownloadFailed += (sender, args) => { AlgorithmHandlers.Results.ErrorMessage(args.Message, args.StackTrace); };
                historyProvider.ReaderErrorDetected += (sender, args) => { AlgorithmHandlers.Results.RuntimeError(args.Message, args.StackTrace); };

                algorithm.HistoryProvider = historyProvider;

                // initialize the default brokerage message handler
                algorithm.BrokerageMessageHandler = factory.CreateBrokerageMessageHandler(algorithm, job, SystemHandlers.Api);

                //Initialize the internal state of algorithm and job: executes the algorithm.Initialize() method.
                initializeComplete = AlgorithmHandlers.Setup.Setup(new SetupHandlerParameters(dataManager.UniverseSelection, algorithm, brokerage, job, AlgorithmHandlers.Results, AlgorithmHandlers.Transactions, AlgorithmHandlers.RealTime));

                // set this again now that we've actually added securities
                AlgorithmHandlers.Results.SetAlgorithm(algorithm);

                // alpha handler needs start/end dates to determine sample step sizes
                AlgorithmHandlers.Alphas.OnAfterAlgorithmInitialized(algorithm);

                //If there are any reasons it failed, pass these back to the IDE.
                if (!initializeComplete || algorithm.ErrorMessages.Count > 0 || AlgorithmHandlers.Setup.Errors.Count > 0)
                {
                    initializeComplete = false;
                    //Get all the error messages: internal in algorithm and external in setup handler.
                    var errorMessage = String.Join(",", algorithm.ErrorMessages);
                    errorMessage += String.Join(",", AlgorithmHandlers.Setup.Errors.Select(e =>
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
                    AlgorithmHandlers.Results.RuntimeError(errorMessage);
                    SystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, AlgorithmStatus.RuntimeError, errorMessage);
                }
            }
            catch (Exception err)
            {
                Log.Error(err);
                var runtimeMessage = "Algorithm.Initialize() Error: " + err.Message + " Stack Trace: " + err;
                AlgorithmHandlers.Results.RuntimeError(runtimeMessage, err.ToString());
                SystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, AlgorithmStatus.RuntimeError, runtimeMessage);
            }


            // log the job endpoints
            Log.Trace("JOB HANDLERS: ");
            Log.Trace("         DataFeed:     " + AlgorithmHandlers.DataFeed.GetType().FullName);
            Log.Trace("         Setup:        " + AlgorithmHandlers.Setup.GetType().FullName);
            Log.Trace("         RealTime:     " + AlgorithmHandlers.RealTime.GetType().FullName);
            Log.Trace("         Results:      " + AlgorithmHandlers.Results.GetType().FullName);
            Log.Trace("         Transactions: " + AlgorithmHandlers.Transactions.GetType().FullName);
            Log.Trace("         Alpha:        " + AlgorithmHandlers.Alphas.GetType().FullName);
            if (algorithm?.HistoryProvider != null)
            {
                Log.Trace("         History Provider:     " + algorithm.HistoryProvider.GetType().FullName);
            }
            if (job is LiveNodePacket) Log.Trace("         Brokerage:      " + brokerage?.GetType().FullName);

            return initializeComplete;
        }

        /// <summary>
        /// Handle an error in the algorithm.Run method.
        /// </summary>
        /// <param name="job">Job we're processing</param>
        /// <param name="err">Error from algorithm stack</param>
        private void HandleAlgorithmError(AlgorithmNodePacket job, Exception err)
        {
            Log.Error(err, "Breaking out of parent try catch:");
            if (AlgorithmHandlers.DataFeed != null) AlgorithmHandlers.DataFeed.Exit();
            if (AlgorithmHandlers.Results != null)
            {
                // perform exception interpretation
                err = _exceptionInterpreter.Interpret(err, _exceptionInterpreter);

                var message = "Runtime Error: " + _exceptionInterpreter.GetExceptionMessageHeader(err);
                Log.Trace("Engine.Run(): Sending runtime error to user...");
                AlgorithmHandlers.Results.LogMessage(message);
                AlgorithmHandlers.Results.RuntimeError(message, err.ToString());
                SystemHandlers.Api.SetAlgorithmStatus(job.AlgorithmId, AlgorithmStatus.RuntimeError, message + " Stack Trace: " + err);
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

        public void Dispose()
        {
            SystemHandlers.Dispose();
            AlgorithmHandlers.Dispose();
        }
    } // End Algorithm Node Core Thread
} // End Namespace
