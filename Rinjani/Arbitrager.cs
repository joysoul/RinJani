﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using static System.Threading.Thread;
using Rinjani.Properties;

namespace Rinjani
{
    public class Arbitrager : IArbitrager
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly List<Order> _activeOrders = new List<Order>();
        private readonly IBrokerAdapterRouter _brokerAdapterRouter;
        private readonly IConfigStore _configStore;
        private readonly IBalanceService _positionService;
        private readonly IQuoteAggregator _quoteAggregator;

        public Arbitrager(IQuoteAggregator quoteAggregator,
            IConfigStore configStore,
            IBalanceService positionService,
            IBrokerAdapterRouter brokerAdapterRouter)
        {
            _quoteAggregator = quoteAggregator ?? throw new ArgumentNullException(nameof(quoteAggregator));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _brokerAdapterRouter = brokerAdapterRouter ?? throw new ArgumentNullException(nameof(brokerAdapterRouter));
            _positionService = positionService ?? throw new ArgumentNullException(nameof(positionService));
        }

        public void Start()
        {
            Log.Info(Resources.StartingArbitrager, nameof(Arbitrager));
            _quoteAggregator.QuoteUpdated += QuoteUpdated;
            Log.Info(Resources.StartedArbitrager, nameof(Arbitrager));
        }

        public void Dispose()
        {
            _positionService?.Dispose();
            _quoteAggregator?.Dispose();
        }

        void HpxBuyOrderDeal()
        {
            var config = _configStore.Config;
            var bestAskHpx = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Hpx)
                .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskHpx == null)
            {
                throw new InvalidOperationException(Resources.NoBestAskWasFound);
            }

            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                throw new InvalidOperationException(Resources.NoBestAskWasFound);
            }
            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice);
            decimal invertedSpread = price - bestAskHpx.Price;
            decimal availableVolume = Util.RoundDown(Math.Min(bestBidZb.Volume, bestAskHpx.Volume), 3);
            decimal allowedSellSize = 1;
            decimal allowedBuySize = 1;
            decimal targetVolume = new[] { availableVolume, config.MaxSize, allowedSellSize, allowedBuySize }.Min();
            targetVolume = Util.RoundDown(targetVolume, 3);
            decimal targetProfit = Math.Round(invertedSpread * targetVolume);
            if (invertedSpread / price > config.ArbitragePoint / 100)
            {
                SpreadAnalysisResult result = new SpreadAnalysisResult
                {
                    BestOrderHpx = new Quote(Broker.Hpx, QuoteSide.Bid, bestAskHpx.Price, bestAskHpx.BasePrice, bestAskHpx.Volume),
                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, bestBidZb.Volume),
                    InvertedSpread = invertedSpread,
                    AvailableVolume = availableVolume,
                    TargetVolume = targetVolume,
                    TargetProfit = targetProfit
                };
                ExecuteOrderHpx(result);
                return;
            }
        }

        void HpxSellOrderDeal()
        {
            var config = _configStore.Config;
            var bestBidHpx = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Hpx)
                .OrderByDescending(q => q.Price).FirstOrDefault();

            ///Hpx买价高于Zb基准价
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                throw new InvalidOperationException(Resources.NoBestAskWasFound);
            }
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
            decimal invertedSpread = bestBidHpx.Price - price;
            decimal availableVolume = Util.RoundDown(Math.Min(bestAskZb.Volume, bestBidHpx.Volume), 3);

            decimal targetVolume = new[] { availableVolume, config.MaxSize}.Min();
            targetVolume = Util.RoundDown(targetVolume, 3);
            decimal targetProfit = Math.Round(invertedSpread * targetVolume);
            if (invertedSpread / price > config.ArbitragePoint / 100)
            {
                SpreadAnalysisResult result = new SpreadAnalysisResult
                {
                    BestOrderHpx = new Quote(Broker.Hpx, QuoteSide.Ask, bestBidHpx.Price, bestBidHpx.BasePrice, bestBidHpx.Volume),
                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, bestAskZb.Volume),
                    InvertedSpread = invertedSpread,
                    AvailableVolume = availableVolume,
                    TargetVolume = targetVolume,
                    TargetProfit = targetProfit
                };
                ExecuteOrderHpx(result);
                return;
            }
        }

        void ZbSellOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                throw new InvalidOperationException(Resources.NoBestAskWasFound);
            }
            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice);
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[0].FilledSize),
            };
            ExecuteOrderZb(result);
            return;
        }

        void ZbBuyOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                throw new InvalidOperationException(Resources.NoBestAskWasFound);
            }
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize),
            };
            ExecuteOrderZb(result);
            return;
        }

        private void Arbitrage()
        {
            Log.Info(Resources.LookingForOpportunity);

            if (_activeOrders.Count == 0)
            {
                ZbFilledSize = 0;
                HpxBuyOrderDeal();
            }
            if (_activeOrders.Count == 0)
            {
                ZbFilledSize = 0;
                HpxSellOrderDeal();
            }
            if (_activeOrders.Count >= 1)
            {
                if (_activeOrders[0].Side == OrderSide.Buy)
                    ZbSellOrderDeal();
                else
                    ZbBuyOrderDeal();
            }
            Log.Info(Resources.LookingForOpportunity);
        }

        private void ExecuteOrderHpx(SpreadAnalysisResult result)
        {
            var config = _configStore.Config;
            var bestOrderHpx = result.BestOrderHpx;
            var invertedSpread = result.InvertedSpread;
            var availableVolume = result.AvailableVolume;
            var targetVolume = result.TargetVolume;
            var targetProfit = result.TargetProfit;

            Log.Info("{0,-17}: {1}", "Hpx Order ", bestOrderHpx);
            Log.Info("{0,-17}: {1}", Resources.Spread, -invertedSpread);
            Log.Info("{0,-17}: {1}", Resources.AvailableVolume, availableVolume);
            Log.Info("{0,-17}: {1}", Resources.TargetVolume, targetVolume);
            Log.Info("{0,-17}: {1}", Resources.ExpectedProfit, targetProfit);

            if (invertedSpread <= 0)
            {
                Log.Info(Resources.NoArbitrageOpportunitySpreadIsNotInverted);
                return;
            }

            Log.Info(Resources.FoundInvertedQuotes);
            if (availableVolume < config.MinSize)
            {
                Log.Info(Resources.AvailableVolumeIsSmallerThanMinSize);
                return;
            }

            if (targetProfit < config.ArbitragePoint / 100 * bestOrderHpx.Price)
            {
                Log.Info(Resources.TargetProfitIsSmallerThanMinProfit);
                return;
            }

            if (config.DemoMode)
            {
                Log.Info(Resources.ThisIsDemoModeNotSendingOrders);
                return;
            }

            Log.Info(Resources.FoundArbitrageOppotunity);

            Log.Info(Resources.SendingOrderTargettingQuote, bestOrderHpx);
            SendOrder(bestOrderHpx, targetVolume, OrderType.Limit);
            if (_activeOrders[0].BrokerOrderId == null)
            {
                Sleep(config.SleepAfterSend);
                _activeOrders.Clear();
                return;
            }
            CheckOrderStateHpx();
        }

        private void ExecuteOrderZb(SpreadAnalysisResult result)
        {
            var config = _configStore.Config;
            var bestOrderZb = result.BestOrderZb;

            Log.Info("{0,-17}: {1}", "Zb Order ", bestOrderZb);

            if (config.DemoMode)
            {
                Log.Info(Resources.ThisIsDemoModeNotSendingOrders);
                return;
            }
            Log.Info(Resources.SendingOrderTargettingQuote, bestOrderZb);
            SendOrder(bestOrderZb, bestOrderZb.Volume, OrderType.Limit);
            while (_activeOrders.Count <= 1|| _activeOrders[_activeOrders.Count-1].BrokerOrderId==null)
            {
                Sleep(config.SleepAfterSend);
                Log.Info("Zb Order failure,Re-order", bestOrderZb);
                ExecuteOrderZb(result);
                return;
            }
            CheckOrderStateZb();
            Sleep(config.SleepAfterSend);
        }

        private void CheckOrderStateHpx()
        {
            var order = _activeOrders[0];
            var config = _configStore.Config;
            try
            {
                _brokerAdapterRouter.Refresh(order);
            }
            catch (Exception ex)
            {
                Log.Warn(ex.Message);
                Log.Debug(ex);
            }

            if (order.Status != OrderStatus.Filled)
            {
                if (order.Side == OrderSide.Buy)
                    Log.Warn(Resources.BuyLegIsNotFilledYetPendingSizeIs, order.PendingSize);
                else
                    Log.Warn(Resources.SellLegIsNotFilledYetPendingSizeIs, order.PendingSize);
            }

            if (order.Status != OrderStatus.Filled)
            {
                _brokerAdapterRouter.Cancel(order);
                if (order.FilledSize < config.MinSize)
                {
                    _activeOrders.Clear();
                    return;
                }
                else
                {
                    order.Size = order.FilledSize;
                    order.Status = OrderStatus.Filled;
                }
            }

            if (order.Status == OrderStatus.Filled)
            {
                Log.Info(Resources.BothLegsAreSuccessfullyFilled);
                Log.Info("Hpx order Fill price is ", order.AverageFilledPrice);
                return;
            }
        }

        private decimal ZbFilledSize = 0;
        private decimal ZbAverageFilledPrice = 0;
        private void CheckOrderStateZb()
        {
            var config = _configStore.Config;
            foreach (var i in Enumerable.Range(1, config.MaxRetryCount))
            {
                var order = _activeOrders[_activeOrders.Count - 1];
                Log.Info(Resources.OrderCheckAttempt, i);
                Log.Info(Resources.CheckingIfBothLegsAreDoneOrNot);

                try
                {
                    _brokerAdapterRouter.Refresh(order);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex.Message);
                    Log.Debug(ex);
                }

                if (order.Status != OrderStatus.Filled)
                {
                    Log.Warn("Zb Leg is not filled yet,pending size is {0}", order.PendingSize);
                    ZbFilledSize = 0;
                    for(int j=1;j< _activeOrders.Count;j++)
                    { 
                        ZbFilledSize += _activeOrders[j].FilledSize;
                    }
                    _quoteAggregator.Aggregate();//更新ticker数据
                    if (order.Side == OrderSide.Buy)
                    {
                        var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                            .OrderByDescending(q => q.Price).FirstOrDefault();
                        if (bestAskZb == null)
                        {
                            throw new InvalidOperationException(Resources.NoBestAskWasFound);
                        }
                        decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
                        SpreadAnalysisResult result = new SpreadAnalysisResult
                        {
                            BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize),
                        };
                        ExecuteOrderZb(result);
                        continue;
                    }
                }

                if (order.Status == OrderStatus.Filled)
                {
                    decimal _spendCash = 0;
                    ZbFilledSize = 0;
                    for (int j = 1; j < _activeOrders.Count; j++)
                    {
                        ZbFilledSize += _activeOrders[j].FilledSize;
                        _spendCash += _activeOrders[j].FilledSize * _activeOrders[j].AverageFilledPrice;
                    }
                    ZbAverageFilledPrice = _spendCash / ZbFilledSize;
                    decimal profit = 0;
                    if (order.Side == OrderSide.Buy)
                    {
                        profit = Math.Round(_activeOrders[0].FilledSize * _activeOrders[0].AverageFilledPrice - _spendCash);
                        Log.Info(Resources.BuyFillPriceIs, ZbAverageFilledPrice);
                        Log.Info(Resources.SellFillPriceIs, _activeOrders[0].AverageFilledPrice);
                    }
                    else
                    {
                        profit = Math.Round(_spendCash - _activeOrders[0].FilledSize * _activeOrders[0].AverageFilledPrice);
                        Log.Info(Resources.SellFillPriceIs, ZbAverageFilledPrice);
                        Log.Info(Resources.BuyFillPriceIs, _activeOrders[0].AverageFilledPrice);
                    }
                    Log.Info(Resources.BothLegsAreSuccessfullyFilled);                   
                    Log.Info(Resources.ProfitIs, profit);
                    break;
                }

                if (i == config.MaxRetryCount)
                {
                    Log.Warn(Resources.MaxRetryCountReachedCancellingThePendingOrders);
                    if (order.Status != OrderStatus.Filled)
                    {
                        _brokerAdapterRouter.Cancel(order);
                    }
                    break;
                }
                Sleep(config.OrderStatusCheckInterval);
            }
        }

        //private void CheckOrderState()
        //{
        //    var buyOrder = _activeOrders.First(x => x.Side == OrderSide.Buy);
        //    var sellOrder = _activeOrders.First(x => x.Side == OrderSide.Sell);
        //    var config = _configStore.Config;
        //    foreach (var i in Enumerable.Range(1, config.MaxRetryCount))
        //    {
        //        Sleep(config.OrderStatusCheckInterval);
        //        Log.Info(Resources.OrderCheckAttempt, i);
        //        Log.Info(Resources.CheckingIfBothLegsAreDoneOrNot);

        //        try
        //        {
        //            _brokerAdapterRouter.Refresh(buyOrder);
        //            _brokerAdapterRouter.Refresh(sellOrder);
        //        }
        //        catch (Exception ex)
        //        {
        //            Log.Warn(ex.Message);
        //            Log.Debug(ex);
        //        }

        //        if (buyOrder.Status != OrderStatus.Filled)
        //        {
        //            Log.Warn(Resources.BuyLegIsNotFilledYetPendingSizeIs, sellOrder.PendingSize);
        //        }
        //        if (sellOrder.Status != OrderStatus.Filled)
        //        {
        //            Log.Warn(Resources.SellLegIsNotFilledYetPendingSizeIs, sellOrder.PendingSize);
        //        }

        //        if (buyOrder.Status == OrderStatus.Filled && sellOrder.Status == OrderStatus.Filled)
        //        {
        //            var profit = Math.Round(sellOrder.FilledSize * sellOrder.AverageFilledPrice -
        //                         buyOrder.FilledSize * buyOrder.AverageFilledPrice);
        //            Log.Info(Resources.BothLegsAreSuccessfullyFilled);
        //            Log.Info(Resources.BuyFillPriceIs, buyOrder.AverageFilledPrice);
        //            Log.Info(Resources.SellFillPriceIs, sellOrder.AverageFilledPrice);
        //            Log.Info(Resources.ProfitIs, profit);
        //            break;
        //        }

        //        if (i == config.MaxRetryCount)
        //        {
        //            Log.Warn(Resources.MaxRetryCountReachedCancellingThePendingOrders);
        //            if (buyOrder.Status != OrderStatus.Filled)
        //            {
        //                _brokerAdapterRouter.Cancel(buyOrder);
        //            }

        //            if (sellOrder.Status != OrderStatus.Filled)
        //            {
        //                _brokerAdapterRouter.Cancel(sellOrder);
        //            }
        //            break;
        //        }
        //    }
        //}

        private void QuoteUpdated(object sender, EventArgs e)
        {
            try
            {
                Log.Info(Util.Hr(20) + "ARBITRAGER" + Util.Hr(20));
                Arbitrage();
                Log.Info(Util.Hr(50));
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Log.Debug(ex);
                if (Environment.UserInteractive)
                {
                    Log.Error(Resources.ArbitragerThreadHasBeenStopped);
                    _positionService.Dispose();
                    Console.ReadLine();
                }
                Environment.Exit(-1);
            }
        }

        private void SendOrder(Quote quote, decimal targetVolume, OrderType orderType)
        {
            var brokerConfig = _configStore.Config.Brokers.First(x => x.Broker == quote.Broker);
            var orderSide = quote.Side == QuoteSide.Ask ? OrderSide.Sell : OrderSide.Buy;
            var cashMarginType = brokerConfig.CashMarginType;
            var leverageLevel = brokerConfig.LeverageLevel;
            var order = new Order(quote.Broker, orderSide, targetVolume, quote.Price, cashMarginType, orderType,
                leverageLevel);
            _brokerAdapterRouter.Send(order);
            _activeOrders.Add(order);
        }
    }
}