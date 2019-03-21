using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using System;

namespace Valyria.Launcher.Algs
{
    public abstract class ValyriaAlgorithm : QCAlgorithm
    {
        /// <summary>
        /// This is used by the regression test system to indicate if the open source Lean repository has the required data to run this algorithm.
        /// </summary>
        public bool CanRunLocally { get; } = true;

        /// <summary>
        /// This is used by the regression test system to indicate which languages this algorithm is written in.
        /// </summary>
        public Language[] Languages { get; } = { Language.CSharp };


        public Indicators.DropRate DR(Symbol symbol, int period, Resolution? resolution = null,
                                    Func<IBaseData, decimal> selector = null)
        {
            var name = CreateIndicatorName(symbol, "DR" + period, resolution);
            var dropRate = new Indicators.DropRate(name, period);
            RegisterIndicator(symbol, dropRate, ResolveConsolidator(symbol, resolution), selector);
            return dropRate;
        }

        public new void RegisterIndicator(Symbol symbol, IndicatorBase<TradeBar> indicator, IDataConsolidator consolidator, Func<IBaseData, decimal> selector = null)
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
    }
}
