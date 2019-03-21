using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Brokerages;
using QuantConnect.Orders;
using QuantConnect.Interfaces;
using QuantConnect;
using QuantConnect.Data.Market;
using QuantConnect.Securities.Crypto;

namespace Valyria.Launcher.Algs
{
    public class FlashCrashIndicators
    {
        public Indicators.DropRate DropRate { get; set; }
    }

    public class MarketParams
    {
        public decimal MinimumDropRate { get; set; }
        public decimal MinimumVolume { get; set; }
        public decimal CrashThreshold { get; set; }
        public decimal FlashPumpThreshold { get; set; }
        public int DropInterval { get; set; }
        public double SellProfit { get; set; }
        public int BuyExpiration { get; set; }
        public int SellExpiration { get; set; }
    }

    public class FlashCrashRunParams : RunParams
    {
        public Dictionary<string, MarketParams> MarketParams { get; set; }
    }

    public class FlashCrashAlgorithm : ValyriaAlgorithm, IRegressionAlgorithmDefinition
    {
        public FlashCrashRunParams RunParams { get; set; }

        public Dictionary<string, Crypto> TradingPairs = new Dictionary<string, Crypto>();
        private Dictionary<string, FlashCrashIndicators> Indicators = new Dictionary<string, FlashCrashIndicators>();

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            SetBrokerageModel(BrokerageName.Binance, AccountType.Cash);

            if (RunParams.StartDate.HasValue)
            {
                SetStartDate(RunParams.StartDate.Value);
            }

            if (RunParams.EndDate.HasValue)
            {
                SetEndDate(RunParams.EndDate.Value);
            }

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

        private void ProcessData(TradeBar data, FlashCrashIndicators indicators, MarketParams marketParams)
        {
            if (indicators.DropRate < marketParams.MinimumDropRate)
            {
                return;
            }

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
                var tradeBar = entry.Value as TradeBar;
                var indicators = Indicators[tradeBar.Symbol.Value];
                var marketParams = RunParams.MarketParams[tradeBar.Symbol.Value];

                ProcessData(tradeBar, indicators, marketParams);
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
        }

        public override void OnEndOfAlgorithm()
        {
        }


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
