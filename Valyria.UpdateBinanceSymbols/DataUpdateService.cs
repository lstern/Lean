using Binance.Net;
using Binance.Net.Objects;
using MessagePack;
using QuantConnect;
using QuantConnect.Data.Market;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

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
            foreach (var symbol in client.GetExchangeInfo().Data.Symbols)
            {
                UpdateMarketData(startDate, endDate, $"{outputFolder}/{symbol.Name}", symbol.Name);
            }
        }

        private void UpdateMarketData(DateTime startDate, DateTime endDate, string outputFolder, string symbol)
        {
            var currentDate = GetLastUpdateTimestamp(startDate, outputFolder, symbol);

            while (currentDate < endDate)
            {
                var candles = GetDayKlines(symbol, currentDate);
                if (candles.Count == 0)
                {
                    Console.WriteLine($"Failed to update {symbol}.");
                    break;
                }

                StoreCandles(symbol, candles, outputFolder);
                currentDate = currentDate.AddDays(1);
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

            var marketFolder = Path.Combine(outputFolder, symbol);

            if (!Directory.Exists(marketFolder))
            {
                Directory.CreateDirectory(marketFolder);
            }

            var lastStoredDate = Directory.GetFiles(marketFolder).OrderByDescending(c => c).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(lastStoredDate))
            {
                return startDate;
            }

            var parts = lastStoredDate.Split('_');

            var date = DateTime.ParseExact(parts[2].Split('.')[0], "yyyyMMdd", CultureInfo.InvariantCulture);
            return date;
        }
    }

    public class Candle
    {
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }
}
