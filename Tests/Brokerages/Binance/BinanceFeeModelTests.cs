using NUnit.Framework;
using QuantConnect.Brokerages;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;
using System;

namespace QuantConnect.Tests.Brokerages.Binance
{
    [TestFixture]
    public class BinanceFeeModelTests
    {
        protected Symbol Symbol => Symbol.Create("ETHUSD", SecurityType.Crypto, Market.Binance);
        protected Security Security
        {
            get
            {
                var security = new Security(
                    SecurityExchangeHours.AlwaysOpen(TimeZones.NewYork),
                    new SubscriptionDataConfig(
                        typeof(TradeBar),
                        Symbol,
                        Resolution.Minute,
                        TimeZones.NewYork,
                        TimeZones.NewYork,
                        false,
                        false,
                        false
                    ),
                    new Cash(Currencies.USD, 0, 1m),
                    SymbolProperties.GetDefault(Currencies.USD),
                    ErrorCurrencyConverter.Instance
                );
                security.SetMarketPrice(new Tick(DateTime.UtcNow, Symbol, LowPrice, HighPrice));

                return security;
            }
        }
        protected OrderSubmissionData OrderSubmissionData => new OrderSubmissionData(Security.BidPrice, Security.AskPrice, (Security.BidPrice + Security.AskPrice) / 2);

        protected decimal HighPrice => 1000m;
        protected decimal LowPrice => 100m;

        protected decimal Quantity => 1m;

        public TestCaseData[] MakerOrders => new[]
        {
            new TestCaseData(new LimitOrderTestParameters(Symbol, HighPrice, LowPrice)),
            new TestCaseData(new LimitOrderTestParameters(Symbol, HighPrice, LowPrice) { OrderSubmissionData = OrderSubmissionData}),
            new TestCaseData(new LimitOrderTestParameters(Symbol, HighPrice, LowPrice, new BinanceOrderProperties())),
            new TestCaseData(new LimitOrderTestParameters(Symbol, LowPrice, HighPrice, new BinanceOrderProperties() { PostOnly = true }){ OrderSubmissionData = OrderSubmissionData}),
            new TestCaseData(new LimitOrderTestParameters(Symbol, HighPrice, LowPrice, new BinanceOrderProperties() { PostOnly = true }))
        };

        public TestCaseData[] TakerOrders => new[]
        {
            new TestCaseData(new MarketOrderTestParameters(Symbol)),
            new TestCaseData(new MarketOrderTestParameters(Symbol, new BinanceOrderProperties() { PostOnly = true })),
            new TestCaseData(new LimitOrderTestParameters(Symbol, LowPrice, HighPrice) { OrderSubmissionData = OrderSubmissionData})
        };

        [Test]
        public void GetFeeModelTest()
        {
            BinanceBrokerageModel model = new BinanceBrokerageModel();
            Assert.IsInstanceOf<BinanceFeeModel>(model.GetFeeModel(Security));
        }

        [Test]
        [TestCaseSource("MakerOrders")]
        public void ReturnShortOrderMakerFees(OrderTestParameters parameters)
        {
            IFeeModel feeModel = new BinanceFeeModel();

            Order order = parameters.CreateShortOrder(Quantity);
            var price = order.Type == OrderType.Limit ? ((LimitOrder)order).LimitPrice : LowPrice;

            Assert.AreEqual(
                BinanceFeeModel.MakerFee * price * Math.Abs(Quantity),
                feeModel.GetOrderFee(Security, order));
        }

        [Test]
        [TestCaseSource("TakerOrders")]
        public void ReturnShortOrderTakerFees(OrderTestParameters parameters)
        {
            IFeeModel feeModel = new BinanceFeeModel();

            Order order = parameters.CreateShortOrder(Quantity);
            var price = order.Type == OrderType.Limit ? ((LimitOrder)order).LimitPrice : LowPrice;

            Assert.AreEqual(
                BinanceFeeModel.TakerFee * price * Math.Abs(Quantity),
                feeModel.GetOrderFee(Security, order));
        }

        [Test]
        [TestCaseSource("MakerOrders")]
        public void ReturnLongOrderMakerFees(OrderTestParameters parameters)
        {
            IFeeModel feeModel = new BinanceFeeModel();

            Order order = parameters.CreateLongOrder(Quantity);
            var price = order.Type == OrderType.Limit ? ((LimitOrder)order).LimitPrice : HighPrice;

            Assert.AreEqual(
                BinanceFeeModel.MakerFee * price * Math.Abs(Quantity),
                feeModel.GetOrderFee(Security, order));
        }

        [Test]
        [TestCaseSource("TakerOrders")]
        public void ReturnLongOrderTakerFees(OrderTestParameters parameters)
        {
            IFeeModel feeModel = new BinanceFeeModel();

            Order order = parameters.CreateLongOrder(Quantity);
            var price = order.Type == OrderType.Limit ? ((LimitOrder)order).LimitPrice : HighPrice;

            Assert.AreEqual(
                BinanceFeeModel.TakerFee * price * Math.Abs(Quantity),
                feeModel.GetOrderFee(Security, order));
        }
    }
}
