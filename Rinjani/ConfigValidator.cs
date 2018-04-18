﻿using Rinjani.Properties;
using System;
using System.Linq;

namespace Rinjani
{
    public class ConfigValidator : IConfigValidator
    {
        public void Validate(ConfigRoot config)
        {
            ThrowIf(config.Brokers.Count < 2, Resources.AtLeastTwoBrokersMustBeEnabled);
            MustBePositive(config.IterationInterval, nameof(config.IterationInterval));
            MustBePositive(config.MaxRetryCount, nameof(config.MaxRetryCount));
            MustBeGreaterThanZero(config.MaxSize, nameof(config.MaxSize));
            MustBeGreaterThanZero(config.MinSize, nameof(config.MinSize));
            MustBePositive(config.ArbitragePoint, nameof(config.ArbitragePoint));
            MustBePositive(config.OrderStatusCheckInterval, nameof(config.OrderStatusCheckInterval));
            MustBePositive(config.BalanceRefreshInterval, nameof(config.BalanceRefreshInterval));
            MustBePositive(config.PriceMergeSize, nameof(config.PriceMergeSize));
            MustBePositive(config.QuoteRefreshInterval, nameof(config.QuoteRefreshInterval));
            MustBePositive(config.SleepAfterSend, nameof(config.SleepAfterSend));
        }

        private void MustBePositive(int number, string name)
        {
            MustBePositive((decimal)number, name);
        }

        private void MustBePositive(decimal number, string name)
        {
            ThrowIf(number <= 0m, $"{name} must be positive.");
        }

        private void MustBeGreaterThanZero(int number, string name)
        {
            MustBeGreaterThanZero((decimal)number, name);
        }

        private void MustBeGreaterThanZero(decimal number, string name)
        {
            ThrowIf(number < 0m, $"{name} must be zero or positive.");
        }

        private bool IsEnabled(BrokerConfig brokerConfig)
        {
            return brokerConfig != null && brokerConfig.Enabled;
        }

        private void ThrowIf(bool condition, string message)
        {
            if (condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}