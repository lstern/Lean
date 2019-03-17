using System;
using QuantConnect.Logging;

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
            var trader = new Trader();
            trader.Initialize(args);
            var job = trader.LoadJob();

            try
            {
                trader.RunJob(job);
            }
            finally
            {
                trader.CleanJob(job);
            }
        }
    }
}
