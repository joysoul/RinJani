﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace Rinjani
{
    public class BrokerAdapterRouter : IBrokerAdapterRouter
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<Broker, IBrokerAdapter> _brokerAdapterMap;

        public BrokerAdapterRouter(IList<IBrokerAdapter> brokerAdapters)
        {
            if (brokerAdapters == null)
            {
                throw new ArgumentNullException(nameof(brokerAdapters));
            }
            _brokerAdapterMap = brokerAdapters.ToDictionary(x => x.Broker);
        }

        public void Send(Order order)
        {
            Log.Debug(order);
            _brokerAdapterMap[order.Broker].Send(order);
        }

        public void Cancel(Order order)
        {
            Log.Debug(order);
            _brokerAdapterMap[order.Broker].Cancel(order);
        }

        public void Refresh(Order order)
        {
            Log.Debug(order);
            _brokerAdapterMap[order.Broker].Refresh(order);
        }

        public string GetOrdersState(int pageIndex, int tradeType, Broker broker)
        {
            return _brokerAdapterMap[broker].GetOrdersState(pageIndex,tradeType);
        }

        public BrokerBalance GetBalance(Broker broker)
        {
            return _brokerAdapterMap[broker].GetBalance();
        }

        public IList<Quote> FetchQuotes(Broker broker)
        {
            return _brokerAdapterMap[broker].FetchQuotes();
        }
    }
}