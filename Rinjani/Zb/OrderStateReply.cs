﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Rinjani.Zb
{
    public class OrderStateReply
    {
        public string currency { get; set; }
        public string id { get; set; }
        public decimal price { get; set; }
        public int status { get; set; }
        public decimal total_amount { get; set; }
        public decimal trade_amount { get; set; }
        public double trade_date { get; set; }
        public decimal trade_money { get; set; }
        public int type { get; set; }

        public void SetOrder(Order order)
        {
            order.BrokerOrderId = id;
            order.FilledSize = trade_amount;
            order.CreationTime = Util.UnixTimeStampToDateTime(trade_date);
            if (order.FilledSize == order.Size)
            {
                order.Status = OrderStatus.Filled;
            }
            else if (order.FilledSize > 0)
            {
                order.Status = OrderStatus.PartiallyFilled;
            }
            order.LastUpdated = DateTime.Now;
        }
    }
}