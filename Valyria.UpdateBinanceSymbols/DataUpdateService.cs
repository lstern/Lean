using Binance.Net;
using Binance.Net.Objects;
using MessagePack;
using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Valyria.BinanceTools
{
    public class DataUpdateService
    {
        private BinanceClient client;

        public DataUpdateService()
        {
            client = new BinanceClient();
        }

        public void UpdateData(DateTime startDate, DateTime endDate, string outputFolder)
        {
            Parallel.ForEach(client.GetExchangeInfo().Data.Symbols,
                new ParallelOptions { MaxDegreeOfParallelism = 3 },
                symbol => { UpdateMarketData(startDate, endDate, $"{outputFolder}/{symbol.Name}", symbol.Name); });
        }

        private void UpdateMarketData(DateTime startDate, DateTime endDate, string outputFolder, string symbol)
        {
            var currentDate = GetLastUpdateTimestamp(startDate, outputFolder, symbol);

            while (currentDate < endDate)
            {
                var candles = GetDayKlines(symbol, currentDate);

                if (candles.Count > 0)
                {
                    StoreCandles(symbol, candles, outputFolder);
                    currentDate = currentDate.AddDays(1);
                }
                else
                {
                    Console.WriteLine("Skipping date for " + symbol);

                    var klines = client.GetKlines(symbol, KlineInterval.OneMinute, currentDate, endDate, 1);
                    if (!klines.Success)
                    {
                        Console.WriteLine("Failed to retrieve " + symbol);
                        break;
                    }

                    if (klines.Data.Length == 0)
                    {
                        break;
                    }

                    currentDate = klines.Data[0].OpenTime.Date;
                }

            }
        }

        private List<BinanceKline> GetDayKlines(string symbol, DateTime day)
        {
            var start = day.Date;
            var end = start.AddDays(1);

            var result = new List<BinanceKline>(1440);

            while (start < end)
            {
                var candles = client.GetKlines(symbol, KlineInterval.OneMinute, start, end, 480);

                if (!candles.Success)
                {
                    Console.WriteLine($"Failed to update {symbol}. {candles.Error.Message}");
                }

                if (candles.Data.Length == 0)
                {
                    Thread.Sleep(100);
                    break;
                }

                result.AddRange(candles.Data);
                start = candles.Data.Last().CloseTime;
            }

            return result;
        }

        private void StoreCandles(string symbol, List<BinanceKline> klines, string outputFolder)
        {
            var candles = klines.Select(ConvertToCandle);
            var day = candles.First().Time.Date.ToString("yyyyMMdd");

            using (var outputFile = File.Create($"{outputFolder}/{day}_quote.valyria"))
            {
                LZ4MessagePackSerializer.Serialize(outputFile, candles, MessagePack.Resolvers.ContractlessStandardResolver.Instance);
            }
        }

        private Candle ConvertToCandle(BinanceKline kline)
        {
            var candle = new Candle
            {
                Time = kline.OpenTime,
                High = kline.High,
                Low = kline.Low,
                Open = kline.Open,
                Close = kline.Close,
                Volume = kline.Volume
            };

            return candle;
        }

        private static DateTime GetLastUpdateTimestamp(DateTime startDate, string outputFolder, string symbol)
        {
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var lastStoredDate = Directory.GetFiles(outputFolder).OrderByDescending(c => c).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(lastStoredDate))
            {
                return startDate;
            }

            var parts = lastStoredDate.Split('\\').Last().Split('_');

            var date = DateTime.ParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture);
            return date;
        }
    }
}
