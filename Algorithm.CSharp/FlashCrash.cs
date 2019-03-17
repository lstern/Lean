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
*/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Brokerages;
using QuantConnect.Indicators;
using QuantConnect.Orders;
using QuantConnect.Interfaces;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// The demonstration algorithm shows some of the most common order methods when working with Crypto assets.
    /// </summary>
    /// <meta name="tag" content="using data" />
    /// <meta name="tag" content="using quantconnect" />
    /// <meta name="tag" content="trading and orders" />
    public class FlashCrash : QCAlgorithm, IRegressionAlgorithmDefinition
    {
        private ExponentialMovingAverage _fast;
        private ExponentialMovingAverage _slow;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates for your algorithm. All algorithms must initialized.
        /// </summary>
        public override void Initialize()
        {
            SetStartDate(2017, 12, 1); // Set Start Date
            SetEndDate(2017, 12, 31); // Set End Date

            SetCash("BTC", 1);

            SetBrokerageModel(BrokerageName.Binance, AccountType.Cash);

            // You can uncomment the following line when live trading with GDAX,
            // to ensure limit orders will only be posted to the order book and never executed as a taker (incurring fees).
            // Please note this statement has no effect in backtesting or paper trading.
            // DefaultOrderProperties = new GDAXOrderProperties { PostOnly = true };

            // Find more symbols here: http://quantconnect.com/data
            AddCrypto("BTCUSDT");
            AddCrypto("ETHUSDT");
            AddCrypto("ETHBTC");

            var symbol = AddCrypto("ETHUSDT").Symbol;

            // create two moving averages
            _fast = EMA(symbol, 30, Resolution.Minute);
            _slow = EMA(symbol, 60, Resolution.Minute);
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
        /// </summary>
        /// <param name="data">Slice object keyed by symbol containing the stock data</param>
        public override void OnData(Slice data)
        {
            //if (Portfolio.CashBook["EUR"].ConversionRate == 0
            //    || Portfolio.CashBook["BTC"].ConversionRate == 0
            //    || Portfolio.CashBook["ETH"].ConversionRate == 0
            //    || Portfolio.CashBook["LTC"].ConversionRate == 0)
            //{
            //    Log($"EUR conversion rate: {Portfolio.CashBook["EUR"].ConversionRate}");
            //    Log($"BTC conversion rate: {Portfolio.CashBook["BTC"].ConversionRate}");
            //    Log($"LTC conversion rate: {Portfolio.CashBook["LTC"].ConversionRate}");
            //    Log($"ETH conversion rate: {Portfolio.CashBook["ETH"].ConversionRate}");

            //    throw new Exception("Conversion rate is 0");
            //}
            if (Time.Hour == 1 && Time.Minute == 0)
            {
                // Sell all ETH holdings with a limit order at 1% above the current price
                var limitPrice = Math.Round(Securities["ETHUSDT"].Price * 1.01m, 2);
                var quantity = Portfolio.CashBook["ETH"].Amount;
                LimitOrder("ETHUSDT", -quantity, limitPrice);
            }
            else if (Time.Hour == 2 && Time.Minute == 0)
            {
                // Submit a buy limit order for BTC at 5% below the current price
                var usdTotal = Portfolio.CashBook["USDT"].Amount;
                var limitPrice = Math.Round(Securities["BTCUSDT"].Price * 0.95m, 2);
                // use only half of our total USD
                var quantity = usdTotal * 0.5m / limitPrice;
                LimitOrder("BTCUSDT", quantity, limitPrice);
            }
            else if (Time.Hour == 2 && Time.Minute == 1)
            {
                // Get current USD available, subtracting amount reserved for buy open orders
                var usdTotal = Portfolio.CashBook["USDT"].Amount;
                var usdReserved = Transactions.GetOpenOrders(x => x.Direction == OrderDirection.Buy && x.Type == OrderType.Limit)
                    .Where(x => x.Symbol == "BTCUSDT" || x.Symbol == "ETHUSDT")
                    .Sum(x => x.Quantity * ((LimitOrder) x).LimitPrice);
                var usdAvailable = usdTotal - usdReserved;

                // Submit a marketable buy limit order for ETH at 1% above the current price
                var limitPrice = Math.Round(Securities["ETHUSDT"].Price * 1.01m, 2);

                // use all of our available USD
                var quantity = usdAvailable / limitPrice;

                // this order will be rejected for insufficient funds
                LimitOrder("ETHUSDT", quantity, limitPrice);

                // use only half of our available USD
                quantity = usdAvailable * 0.5m / limitPrice;
                LimitOrder("ETHUSDT", quantity, limitPrice);
            }
            else if (Time.Hour == 11 && Time.Minute == 0)
            {
                // Liquidate our BTC holdings (including the initial holding)
                SetHoldings("BTCUSDT", 0m);
            }
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            Debug(Time + " " + orderEvent);
        }

        public override void OnEndOfAlgorithm()
        {
            Log($"{Time} - TotalPortfolioValue: {Portfolio.TotalPortfolioValue}");
            Log($"{Time} - CashBook: {Portfolio.CashBook}");
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
