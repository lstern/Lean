using System;
using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Brokerages;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Interfaces;
using QuantConnect.Algorithm;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Data.Consolidators;
using QuantConnect.Securities.Crypto;

namespace Valyria.Launcher.Algs
{
    public class FlashCrashIndicators
    {
        public DropRate DropRate { get; set; }
    }

    public class FlashCrashRunParams : RunParams
    {

    }

    public class FlashCrash : QCAlgorithm, IRegressionAlgorithmDefinition
    {
        public RunParams RunParams { get; set; }

        public Dictionary<string, Crypto> TradingPairs = new Dictionary<string, Crypto>();
        private Dictionary<string, FlashCrashIndicators> Indicators = new Dictionary<string, FlashCrashIndicators>();


        public DropRate DR(Symbol symbol, int period, Resolution? resolution = null,
                                    Func<IBaseData, decimal> selector = null)
        {
            var name = CreateIndicatorName(symbol, "DR" + period, resolution);
            var dropRate = new DropRate(name, period);
            RegisterIndicator(symbol, dropRate, ResolveConsolidator(symbol, resolution), selector);
            return dropRate;
        }

        public void RegisterIndicator(Symbol symbol, IndicatorBase<TradeBar> indicator, IDataConsolidator consolidator, Func<IBaseData, decimal> selector = null)
        {
            // default our selector to the Value property on BaseData
            selector ??= (x => x.Value);

            // register the consolidator for automatic updates via SubscriptionManager
            SubscriptionManager.AddConsolidator(symbol, consolidator);

            // attach to the DataConsolidated event so it updates our indicator
            consolidator.DataConsolidated += (sender, consolidated) =>
            {
                var value = selector(consolidated);
                indicator.Update(new TradeBar(consolidated as TradeBar));
            };
        }

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            SetBrokerageModel(BrokerageName.Binance, AccountType.Cash);

            //SetStartDate(RunParams.StartDate);
            //if (RunParams.EndDate.HasValue)
            //{
            //    SetEndDate(RunParams.EndDate.Value);
            //}

            foreach (var balance in RunParams.InitialBalance)
            {
                SetCash(balance.Asset, balance.Value);
            }

            foreach (var pair in RunParams.ValidTradingPairs)
            {
                var crypto = AddCrypto(pair);
                TradingPairs[pair] = crypto;

                var indicator = new FlashCrashIndicators
                {
                    DropRate = DR(crypto.Symbol, 5, Resolution.Minute)
                };

                Indicators[pair] = indicator;
            }
        }

        private void ProcessData(TradeBar data)
        {
            // data..
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            foreach (var entry in data)
            {
                ProcessData(entry.Value as TradeBar);
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
        }

        public override void OnEndOfAlgorithm()
        {
        }

        /// <summary>
        /// This is used by the regression test system to indicate if the open source Lean repository has the required data to run this algorithm.
        /// </summary>
        public bool CanRunLocally { get; } = true;

        /// <summary>
        /// This is used by the regression test system to indicate which languages this algorithm is written in.
        /// </summary>
        public Language[] Languages { get; } = { Language.CSharp, Language.Python };

        /// <summary>
        /// This is used by the regression test system to indicate what the expected statistics are from running the algorithm
        /// </summary>
        public Dictionary<string, string> ExpectedStatistics => new Dictionary<string, string>
        {
            {"Total Trades", "10"},
            {"Average Win", "0%"},
            {"Average Loss", "-0.13%"},
            {"Compounding Annual Return", "-99.979%"},
            {"Drawdown", "3.500%"},
            {"Expectancy", "-1"},
            {"Net Profit", "-2.288%"},
            {"Sharpe Ratio", "-11.335"},
            {"Loss Rate", "100%"},
            {"Win Rate", "0%"},
            {"Profit-Loss Ratio", "0"},
            {"Alpha", "-5.739"},
            {"Beta", "413.859"},
            {"Annual Standard Deviation", "0.254"},
            {"Annual Variance", "0.065"},
            {"Information Ratio", "-11.39"},
            {"Tracking Error", "0.254"},
            {"Treynor Ratio", "-0.007"},
            {"Total Fees", "$85.34"}
        };
    }
}
