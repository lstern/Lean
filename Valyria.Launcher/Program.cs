using System;
using QuantConnect.Algorithm.CSharp;
using QuantConnect.Logging;
using Valyria.Launcher.Algs;

namespace Valyria.Launcher
{
    public class Program
    {
        static Program()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (sender, e) =>
            {
                if (e.LoadedAssembly.FullName.ToLower().Contains("python"))
                {
                    Log.Trace($"Python for .NET Assembly: {e.LoadedAssembly.GetName()}");
                }
            };
        }

        static void Main(string[] args)
        {
            var trader = new Trader(args);

            var job = trader.LoadJob();

            try
            {
                // trader.RunJob(job);
                var init = new RunParams()
                {
                    StartDate = new DateTime(2018, 4, 4),
                    EndDate = new DateTime(2018, 5, 4),
                    InitialBalance = new Balance[] { new Balance { Asset = "BTC", Value = 1m } },
                    ValidTradingPairs = new[] { "BTCUSDT", "ETHUSDT", "ETHBTC" }
                };

                var task = new FlashCrash();
                task.RunParams = init;

                trader.RunJob(job, task);
            }
            finally
            {
                trader.CleanJob(job);
            }
        }
    }
}
