using Binance.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Valyria.BinanceTools;

namespace Valyria.UpdateBinanceSymbols
{
    class Program
    {
        static void Main(string[] args)
        {
            var option = args.Length > 0 ? args[0] : "update-data";
            switch (option)
            {
                case "update-symbols":
                    UpdateSymbols();
                    break;
                case "update-data":
                    var service = new DataUpdateService();
                    service.UpdateData(new DateTime(2017, 7, 17), DateTime.Today.AddDays(-1), @"D:/Peregrinvs/Lean/Data/crypto/binance/minute");
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
