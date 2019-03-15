using Binance.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;

namespace Valyria.UpdateBinanceSymbols
{
    class Program
    {
        static void Main(string[] args)
        {
            switch (args[0])
            {
                case "update-symbols":
                default:
                    UpdateSymbols();
                    break;
            }
        }

        private static void UpdateSymbols()
        {
            var client = new BinanceClient();
            var info = client.GetExchangeInfo();

            using (StreamWriter outputFile = new StreamWriter("binance-symbols.csv"))
            {
                foreach (var symbol in info.Data.Symbols)
                {
                    outputFile.WriteLine($"binance,{symbol.Name},crypto,{symbol.Name},{symbol.QuoteAsset},1,{symbol.PriceFilter.MinPrice},{symbol.LotSizeFilter.StepSize}");
                }
            }
        }
    }
}
