﻿/*
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
using System.Linq;

namespace QuantConnect.Brokerages.Binance
{
    /// <summary>
    /// Provides the mapping between Lean symbols and Binance symbols.
    /// </summary>
    public class BinanceSymbolMapper : ISymbolMapper
    {
        /// <summary>
        /// Symbols that are both active and delisted
        /// </summary>
        public static List<Symbol> KnownSymbols
        {
            get
            {
                var symbols = new List<Symbol>();
                var mapper = new BinanceSymbolMapper();
                foreach (var tp in KnownSymbolStrings)
                {
                    symbols.Add(mapper.GetLeanSymbol(tp, mapper.GetBrokerageSecurityType(tp), Market.Binance));
                }
                return symbols;
            }
        }

        /// <summary>
        /// The list of known Binance symbols.
        /// https://api.Binance.com/v1/symbols
        /// </summary>
        public static readonly HashSet<string> KnownSymbolStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ETHBTC", "LTCBTC", "BNBBTC", "NEOBTC", "QTUMETH", "EOSETH", "SNTETH", "BNTETH", "BCCBTC", "GASBTC",
            "BNBETH", "BTCUSDT", "ETHUSDT", "HSRBTC", "OAXETH", "DNTETH", "MCOETH", "ICNETH", "MCOBTC", "WTCBTC",
            "WTCETH", "LRCBTC", "LRCETH", "QTUMBTC", "YOYOBTC", "OMGBTC", "OMGETH", "ZRXBTC", "ZRXETH", "STRATBTC",
            "STRATETH", "SNGLSBTC", "SNGLSETH", "BQXBTC", "BQXETH", "KNCBTC", "KNCETH", "FUNBTC", "FUNETH", "SNMBTC",
            "SNMETH", "NEOETH", "IOTABTC", "IOTAETH", "LINKBTC", "LINKETH", "XVGBTC", "XVGETH", "SALTBTC", "SALTETH",
            "MDABTC", "MDAETH", "MTLBTC", "MTLETH", "SUBBTC", "SUBETH", "EOSBTC", "SNTBTC", "ETCETH", "ETCBTC",
            "MTHBTC", "MTHETH", "ENGBTC", "ENGETH", "DNTBTC", "ZECBTC", "ZECETH", "BNTBTC", "ASTBTC", "ASTETH",
            "DASHBTC", "DASHETH", "OAXBTC", "ICNBTC", "BTGBTC", "BTGETH", "EVXBTC", "EVXETH", "REQBTC", "REQETH",
            "VIBBTC", "VIBETH", "HSRETH", "TRXBTC", "TRXETH", "POWRBTC", "POWRETH", "ARKBTC", "ARKETH", "YOYOETH",
            "XRPBTC", "XRPETH", "MODBTC", "MODETH", "ENJBTC", "ENJETH", "STORJBTC", "STORJETH", "BNBUSDT", "VENBNB",
            "YOYOBNB", "POWRBNB", "VENBTC", "VENETH", "KMDBTC", "KMDETH", "NULSBNB", "RCNBTC", "RCNETH", "RCNBNB",
            "NULSBTC", "NULSETH", "RDNBTC", "RDNETH", "RDNBNB", "XMRBTC", "XMRETH", "DLTBNB", "WTCBNB", "DLTBTC",
            "DLTETH", "AMBBTC", "AMBETH", "AMBBNB", "BCCETH", "BCCUSDT", "BCCBNB", "BATBTC", "BATETH", "BATBNB",
            "BCPTBTC", "BCPTETH", "BCPTBNB", "ARNBTC", "ARNETH", "GVTBTC", "GVTETH", "CDTBTC", "CDTETH", "GXSBTC",
            "GXSETH", "NEOUSDT", "NEOBNB", "POEBTC", "POEETH", "QSPBTC", "QSPETH", "QSPBNB", "BTSBTC", "BTSETH",
            "BTSBNB", "XZCBTC", "XZCETH", "XZCBNB", "LSKBTC", "LSKETH", "LSKBNB", "TNTBTC", "TNTETH", "FUELBTC",
            "FUELETH", "MANABTC", "MANAETH", "BCDBTC", "BCDETH", "DGDBTC", "DGDETH", "IOTABNB", "ADXBTC", "ADXETH",
            "ADXBNB", "ADABTC", "ADAETH", "PPTBTC", "PPTETH", "CMTBTC", "CMTETH", "CMTBNB", "XLMBTC", "XLMETH",
            "XLMBNB", "CNDBTC", "CNDETH", "CNDBNB", "LENDBTC", "LENDETH", "WABIBTC", "WABIETH", "WABIBNB", "LTCETH",
            "LTCUSDT", "LTCBNB", "TNBBTC", "TNBETH", "WAVESBTC", "WAVESETH", "WAVESBNB", "GTOBTC", "GTOETH", "GTOBNB",
            "ICXBTC", "ICXETH", "ICXBNB", "OSTBTC", "OSTETH", "OSTBNB", "ELFBTC", "ELFETH", "AIONBTC", "AIONETH",
            "AIONBNB", "NEBLBTC", "NEBLETH", "NEBLBNB", "BRDBTC", "BRDETH", "BRDBNB", "MCOBNB", "EDOBTC", "EDOETH",
            "WINGSBTC", "WINGSETH", "NAVBTC", "NAVETH", "NAVBNB", "LUNBTC", "LUNETH", "TRIGBTC", "TRIGETH", "TRIGBNB",
            "APPCBTC", "APPCETH", "APPCBNB", "VIBEBTC", "VIBEETH", "RLCBTC", "RLCETH", "RLCBNB", "INSBTC", "INSETH",
            "PIVXBTC", "PIVXETH", "PIVXBNB", "IOSTBTC", "IOSTETH", "CHATBTC", "CHATETH", "STEEMBTC", "STEEMETH",
            "STEEMBNB", "NANOBTC", "NANOETH", "NANOBNB", "VIABTC", "VIAETH", "VIABNB", "BLZBTC", "BLZETH", "BLZBNB",
            "AEBTC", "AEETH", "AEBNB", "RPXBTC", "RPXETH", "RPXBNB", "NCASHBTC", "NCASHETH", "NCASHBNB", "POABTC",
            "POAETH", "POABNB", "ZILBTC", "ZILETH", "ZILBNB", "ONTBTC", "ONTETH", "ONTBNB", "STORMBTC", "STORMETH",
            "STORMBNB", "QTUMBNB", "QTUMUSDT", "XEMBTC", "XEMETH", "XEMBNB", "WANBTC", "WANETH", "WANBNB", "WPRBTC",
            "WPRETH", "QLCBTC", "QLCETH", "SYSBTC", "SYSETH", "SYSBNB", "QLCBNB", "GRSBTC", "GRSETH", "ADAUSDT",
            "ADABNB", "CLOAKBTC", "CLOAKETH", "GNTBTC", "GNTETH", "GNTBNB", "LOOMBTC", "LOOMETH", "LOOMBNB", "XRPUSDT",
            "BCNBTC", "BCNETH", "BCNBNB", "REPBTC", "REPETH", "REPBNB", "TUSDBTC", "TUSDETH", "TUSDBNB", "ZENBTC",
            "ZENETH", "ZENBNB", "SKYBTC", "SKYETH", "SKYBNB", "EOSUSDT", "EOSBNB", "CVCBTC", "CVCETH", "CVCBNB",
            "THETABTC", "THETAETH", "THETABNB", "XRPBNB", "TUSDUSDT", "IOTAUSDT", "XLMUSDT", "IOTXBTC", "IOTXETH",
            "QKCBTC", "QKCETH", "AGIBTC", "AGIETH", "AGIBNB", "NXSBTC", "NXSETH", "NXSBNB", "ENJBNB", "DATABTC",
            "DATAETH", "ONTUSDT", "TRXUSDT", "ETCUSDT", "ETCBNB", "ICXUSDT", "SCBTC", "SCETH", "SCBNB", "NPXSBTC",
            "NPXSETH", "VENUSDT", "KEYBTC", "KEYETH", "NASBTC", "NASETH", "NASBNB", "MFTBTC", "MFTETH", "MFTBNB",
            "DENTBTC", "DENTETH", "ARDRBTC", "ARDRETH", "ARDRBNB", "NULSUSDT", "HOTBTC", "HOTETH", "VETBTC", "VETETH",
            "VETUSDT", "VETBNB", "DOCKBTC", "DOCKETH", "POLYBTC", "POLYBNB", "PHXBTC", "PHXETH", "PHXBNB", "HCBTC",
            "HCETH", "GOBTC", "GOBNB"
        };

        /// <summary>
        /// The list of known Binance currencies.
        /// </summary>
        private static readonly HashSet<string> KnownFiatCurrencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "USD"
        };

        /// <summary>
        /// Converts a Lean symbol instance to an Binance symbol
        /// </summary>
        /// <param name="symbol">A Lean symbol instance</param>
        /// <returns>The Binance symbol</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            if (symbol == null || string.IsNullOrWhiteSpace(symbol.Value))
                throw new ArgumentException("Invalid symbol: " + (symbol == null ? "null" : symbol.ToString()));

            if (symbol.SecurityType != SecurityType.Crypto)
                throw new ArgumentException("Invalid security type: " + symbol.SecurityType);

            var brokerageSymbol = ConvertLeanSymbolToBrokerageSymbol(symbol.Value);

            if (!IsKnownBrokerageSymbol(brokerageSymbol))
                throw new ArgumentException("Unknown symbol: " + symbol.Value);

            return brokerageSymbol;
        }

        /// <summary>
        /// Converts an Binance symbol to a Lean symbol instance
        /// </summary>
        /// <param name="brokerageSymbol">The Binance symbol</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">Expiration date of the security(if applicable)</param>
        /// <param name="strike">The strike of the security (if applicable)</param>
        /// <param name="optionRight">The option right of the security (if applicable)</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(string brokerageSymbol, SecurityType securityType, string market, DateTime expirationDate = default(DateTime), decimal strike = 0, OptionRight optionRight = 0)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
                throw new ArgumentException($"Invalid Binance symbol: {brokerageSymbol}");

            if (!IsKnownBrokerageSymbol(brokerageSymbol))
                throw new ArgumentException($"Unknown Binance symbol: {brokerageSymbol}");

            if (securityType != SecurityType.Crypto)
                throw new ArgumentException($"Invalid security type: {securityType}");

            if (market != Market.Binance)
                throw new ArgumentException($"Invalid market: {market}");

            return Symbol.Create(ConvertBrokerageSymbolToLeanSymbol(brokerageSymbol), GetBrokerageSecurityType(brokerageSymbol), Market.Binance);
        }

        /// <summary>
        /// Converts an Binance symbol to a Lean symbol instance
        /// </summary>
        /// <param name="brokerageSymbol">The Binance symbol</param>
        /// <returns>A new Lean Symbol instance</returns>
        public Symbol GetLeanSymbol(string brokerageSymbol)
        {
            var securityType = GetBrokerageSecurityType(brokerageSymbol);
            return GetLeanSymbol(brokerageSymbol, securityType, Market.Binance);
        }

        /// <summary>
        /// Returns the security type for an Binance symbol
        /// </summary>
        /// <param name="brokerageSymbol">The Binance symbol</param>
        /// <returns>The security type</returns>
        public SecurityType GetBrokerageSecurityType(string brokerageSymbol)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
                throw new ArgumentException($"Invalid Binance symbol: {brokerageSymbol}");

            if (!IsKnownBrokerageSymbol(brokerageSymbol))
                throw new ArgumentException($"Unknown Binance symbol: {brokerageSymbol}");

            return SecurityType.Crypto;
        }

        /// <summary>
        /// Returns the security type for a Lean symbol
        /// </summary>
        /// <param name="leanSymbol">The Lean symbol</param>
        /// <returns>The security type</returns>
        public SecurityType GetLeanSecurityType(string leanSymbol)
        {
            return GetBrokerageSecurityType(ConvertLeanSymbolToBrokerageSymbol(leanSymbol));
        }

        /// <summary>
        /// Checks if the symbol is supported by Binance
        /// </summary>
        /// <param name="brokerageSymbol">The Binance symbol</param>
        /// <returns>True if Binance supports the symbol</returns>
        public bool IsKnownBrokerageSymbol(string brokerageSymbol)
        {
            if (string.IsNullOrWhiteSpace(brokerageSymbol))
                return false;

            return KnownSymbolStrings.Contains(brokerageSymbol);
        }

        /// <summary>
        /// Checks if the currency is supported by Binance
        /// </summary>
        /// <returns>True if Binance supports the currency</returns>
        public bool IsPeggedFiatCurrency(string currency)
        {
            if (string.IsNullOrWhiteSpace(currency))
                return false;

            return KnownFiatCurrencies.Any(fiat => string.Equals($"T{fiat}", currency) || string.Equals($"{fiat}T", currency));
        }

        /// <summary>
        /// Checks if the symbol is supported by Binance
        /// </summary>
        /// <param name="symbol">The Lean symbol</param>
        /// <returns>True if Binance supports the symbol</returns>
        public bool IsKnownLeanSymbol(Symbol symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol?.Value) || symbol.Value.Length <= 3)
                return false;

            var BinanceSymbol = ConvertLeanSymbolToBrokerageSymbol(symbol.Value);

            return IsKnownBrokerageSymbol(BinanceSymbol) && GetBrokerageSecurityType(BinanceSymbol) == symbol.SecurityType;
        }

        /// <summary>
        /// Converts an Binance symbol to a Lean symbol string
        /// </summary>
        private static string ConvertBrokerageSymbolToLeanSymbol(string BinanceSymbol)
        {
            if (string.IsNullOrWhiteSpace(BinanceSymbol))
                throw new ArgumentException($"Invalid Binance symbol: {BinanceSymbol}");

            // return as it is due to Binance has similar Symbol format
            return BinanceSymbol.ToUpper();
        }

        /// <summary>
        /// Converts a Lean symbol string to an Binance symbol
        /// </summary>
        private static string ConvertLeanSymbolToBrokerageSymbol(string leanSymbol)
        {
            if (string.IsNullOrWhiteSpace(leanSymbol))
                throw new ArgumentException($"Invalid Lean symbol: {leanSymbol}");

            // return as it is due to Binance has similar Symbol format
            return leanSymbol.ToUpper();
        }
    }
}
