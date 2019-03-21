using QuantConnect.Data.Market;
using QuantConnect.Indicators;
using System.Linq;

namespace Valyria.Indicators
{
    /// <summary>
    /// This indicator computes the Drop Rate (DR). 
    /// The Rate Of Change Ratio is calculated with the following formula:
    /// DR = high / close
    /// </summary>
    public class DropRate : WindowIndicator<TradeBar>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DropRate"/> class using the specified name and period.
        /// </summary> 
        /// <param name="name">The name of this indicator</param>
        /// <param name="period">The period of the ROCR</param>
        public DropRate(string name, int period)
            : base(name, period)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DropRate"/> class using the specified period.
        /// </summary> 
        /// <param name="period">The period of the ROCR</param>
        public DropRate(int period)
            : base("DR" + period, period)
        {
        }

        /// <summary>
        /// Computes the next value for this indicator from the given state.
        /// </summary>
        /// <param name="window">The window of data held in this indicator</param>
        /// <param name="input">The input value to this indicator on this time step</param>
        /// <returns>A new value for this indicator</returns>
        protected override decimal ComputeNextValue(IReadOnlyWindow<TradeBar> window, TradeBar input)
        {
            var max = window.Max(w => w.High);

            return (max - input.Close) / input.Close;
        }
    }
}
