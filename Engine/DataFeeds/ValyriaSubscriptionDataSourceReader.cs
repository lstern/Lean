/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds.Transport;
using QuantConnect.Util;
using System.Runtime.Caching;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.UniverseSelection;
using MessagePack;
using QuantConnect.Data.Market;
using System.IO;

namespace QuantConnect.Lean.Engine.DataFeeds
{
    /// <summary>
    /// Provides an implementations of <see cref="ISubscriptionDataSourceReader"/> that uses the
    /// <see cref="BaseData.Reader(QuantConnect.Data.SubscriptionDataConfig,string,System.DateTime,bool)"/>
    /// method to read lines of text from a <see cref="SubscriptionDataSource"/>
    /// </summary>
    public class ValyriaSubscriptionDataSourceReader : ISubscriptionDataSourceReader
    {
        private readonly bool _isLiveMode;
        private readonly BaseData _factory;
        private readonly DateTime _date;
        private readonly SubscriptionDataConfig _config;
        private bool _shouldCacheDataPoints;
        private readonly IDataCacheProvider _dataCacheProvider;
        private static readonly MemoryCache BaseDataSourceCache = new MemoryCache("BaseDataSourceCache",
            // Cache can use up to 70% of the installed physical memory
            new NameValueCollection { { "physicalMemoryLimitPercentage", "70" } });
        private static readonly CacheItemPolicy CachePolicy = new CacheItemPolicy
        {
            // Cache entry should be evicted if it has not been accessed in given span of time:
            SlidingExpiration = TimeSpan.FromMinutes(5)
        };

        /// <summary>
        /// Event fired when the specified source is considered invalid, this may
        /// be from a missing file or failure to download a remote source
        /// </summary>
        public event EventHandler<InvalidSourceEventArgs> InvalidSource;

        /// <summary>
        /// Event fired when an exception is thrown during a call to
        /// <see cref="BaseData.Reader(QuantConnect.Data.SubscriptionDataConfig,string,System.DateTime,bool)"/>
        /// </summary>
        public event EventHandler<ReaderErrorEventArgs> ReaderError;

        /// <summary>
        /// Event fired when there's an error creating an <see cref="IStreamReader"/> or the
        /// instantiated <see cref="IStreamReader"/> has no data.
        /// </summary>
        public event EventHandler<CreateStreamReaderErrorEventArgs> CreateStreamReaderError;


        /// <summary>
        /// Initializes a new instance of the <see cref="ValyriaSubscriptionDataSourceReader"/> class
        /// </summary>
        /// <param name="dataCacheProvider">This provider caches files if needed</param>
        /// <param name="config">The subscription's configuration</param>
        /// <param name="date">The date this factory was produced to read data for</param>
        /// <param name="isLiveMode">True if we're in live mode, false for backtesting</param>
        public ValyriaSubscriptionDataSourceReader(IDataCacheProvider dataCacheProvider, SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            _dataCacheProvider = dataCacheProvider;
            _date = date;
            _config = config;
            _isLiveMode = isLiveMode;
            _factory = (BaseData)ObjectActivator.GetActivator(config.Type).Invoke(new object[] { config.Type });
            _shouldCacheDataPoints = !_config.IsCustomData && _config.Resolution >= Resolution.Hour
                && _config.Type != typeof(FineFundamental) && _config.Type != typeof(CoarseFundamental);
        }

        private BaseData ConverToTradeBar(Candle candle)
        {
            var tradeBar = new TradeBar
            {
                Symbol = _config.Symbol,
                Period = _config.Increment,
                Close = candle.Close,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Volume = candle.Volume,
                Time = candle.Time
            };

            return tradeBar;
        }

        /// <summary>
        /// Reads the specified <paramref name="source"/>
        /// </summary>
        /// <param name="source">The source to be read</param>
        /// <returns>An <see cref="IEnumerable{BaseData}"/> that contains the data in the source</returns>
        public IEnumerable<BaseData> Read(SubscriptionDataSource source)
        {
            List<BaseData> cache;
            _shouldCacheDataPoints = _shouldCacheDataPoints &&
                // only cache local files
                source.TransportMedium == SubscriptionTransportMedium.LocalFile;
            var cacheItem = _shouldCacheDataPoints
                ? BaseDataSourceCache.GetCacheItem(source.Source + _config.Type) : null;
            if (cacheItem == null)
            {
                cache = new List<BaseData>();
                using (var reader = new FileStream(source.Source, FileMode.Open, FileAccess.Read))
                {
                    var candles = LZ4MessagePackSerializer.Deserialize<List<Candle>>(reader);
                    cache = candles.Select(ConverToTradeBar).ToList();
                }

                if (!_shouldCacheDataPoints)
                {
                    yield break;
                }

                cacheItem = new CacheItem(source.Source + _config.Type, cache);
                BaseDataSourceCache.Add(cacheItem, CachePolicy);
            }
            cache = cacheItem.Value as List<BaseData>;
            if (cache == null)
            {
                throw new InvalidOperationException("CacheItem can not be cast into expected type. " +
                    $"Type is: {cacheItem.Value.GetType()}");
            }
            // Find the first data point 10 days (just in case) before the desired date
            // and subtract one item (just in case there was a time gap and data.Time is after _date)
            var index = cache.FindIndex(data => data.Time.AddDays(10) > _date);
            index = index > 0 ? (index - 1) : 0;
            foreach (var data in cache.Skip(index))
            {
                var clone = data.Clone();
                clone.Symbol = _config.Symbol;
                yield return clone;
            }
        }


        /// <summary>
        /// Event invocator for the <see cref="InvalidSource"/> event
        /// </summary>
        /// <param name="source">The <see cref="SubscriptionDataSource"/> that was invalid</param>
        /// <param name="exception">The exception if one was raised, otherwise null</param>
        private void OnInvalidSource(SubscriptionDataSource source, Exception exception)
        {
            var handler = InvalidSource;
            if (handler != null) handler(this, new InvalidSourceEventArgs(source, exception));
        }

        /// <summary>
        /// Event invocator for the <see cref="ReaderError"/> event
        /// </summary>
        /// <param name="line">The line that caused the exception</param>
        /// <param name="exception">The exception that was caught</param>
        private void OnReaderError(string line, Exception exception)
        {
            var handler = ReaderError;
            if (handler != null) handler(this, new ReaderErrorEventArgs(line, exception));
        }

        /// <summary>
        /// Event invocator for the <see cref="CreateStreamReaderError"/> event
        /// </summary>
        /// <param name="date">The date of the source</param>
        /// <param name="source">The source that caused the error</param>
        private void OnCreateStreamReaderError(DateTime date, SubscriptionDataSource source)
        {
            var handler = CreateStreamReaderError;
            if (handler != null) handler(this, new CreateStreamReaderErrorEventArgs(date, source));
        }

        /// <summary>
        /// Opens up an IStreamReader for a local file source
        /// </summary>
        private IStreamReader HandleLocalFileSource(SubscriptionDataSource source)
        {
            // handles zip or text files
            return new LocalFileSubscriptionStreamReader(_dataCacheProvider, source.Source);
        }

        /// <summary>
        /// Opens up an IStreamReader for a remote file source
        /// </summary>
        private IStreamReader HandleRemoteSourceFile(SubscriptionDataSource source)
        {
            SubscriptionDataSourceReader.CheckRemoteFileCache();

            try
            {
                // this will fire up a web client in order to download the 'source' file to the cache
                return new RemoteFileSubscriptionStreamReader(_dataCacheProvider, source.Source, Globals.Cache, source.Headers);
            }
            catch (Exception err)
            {
                OnInvalidSource(source, err);
                return null;
            }
        }
    }
}