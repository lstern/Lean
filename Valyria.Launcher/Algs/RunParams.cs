using System;
using System.Collections.Generic;

namespace Valyria.Launcher.Algs
{
    public class RunParams
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public IEnumerable<Balance> InitialBalance { get; set; }
        public IEnumerable<string> ValidTradingPairs { get; set; }
    }

    public class Balance
    {
        public string Asset { get; set; }
        public decimal Value { get; set; }
    }
}
