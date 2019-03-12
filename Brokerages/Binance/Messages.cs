﻿using Newtonsoft.Json;
using System;

namespace QuantConnect.Brokerages.Binance.Messages
{
#pragma warning disable 1591

    public class AccountInformation
    {
        public Balance[] Balances { get; set; }

        public class Balance
        {
            public string Asset { get; set; }
            public decimal Free { get; set; }
            public decimal Locked { get; set; }
            public decimal Amount => Free + Locked;
        }
    }

    public class PriceTicker
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
    }

    public class Order
    {
        [JsonProperty("orderId")]
        public string Id { get; set; }
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        [JsonProperty("origQty")]
        public decimal OriginalAmount { get; set; }
        [JsonProperty("executedQty")]
        public decimal ExecutedAmount { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public string Side { get; set; }

        public decimal Quantity => string.Equals(Side, "buy", StringComparison.OrdinalIgnoreCase) ? OriginalAmount : -OriginalAmount;
    }

    public class OpenOrder : Order
    {
        public long Time { get; set; }
    }

    public class NewOrder : Order
    {
        [JsonProperty("transactTime")]
        public long TransactionTime { get; set; }
    }

    public class BaseMessage
    {
        [JsonProperty("e")]
        public string @Event { get; set; }

        [JsonProperty("E")]
        public long Time { get; set; }

        [JsonProperty("s")]
        public string Symbol { get; set; }
    }

    public class OrderBookSnapshotMessage
    {
        public long LastUpdateId { get; set; }

        public object[][] Bids { get; set; }

        public object[][] Asks { get; set; }
    }

    public class OrderBookUpdateMessage : BaseMessage
    {
        [JsonProperty("U")]
        public long FirstUpdate { get; set; }

        [JsonProperty("u")]
        public long FinalUpdate { get; set; }

        [JsonProperty("b")]
        public object[][] Bids { get; set; }

        [JsonProperty("a")]
        public object[][] Asks { get; set; }
    }

    public class Trade : BaseMessage
    {
        [JsonProperty("T")]
        public new long Time { get; set; }

        [JsonProperty("p")]
        public decimal Price { get; private set; }

        [JsonProperty("q")]
        public decimal Quantity { get; private set; }
    }
#pragma warning restore 1591
}
