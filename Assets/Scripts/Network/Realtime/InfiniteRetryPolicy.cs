using System;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Политика повторных подключений для WithAutomaticReconnect: НИКОГДА не сдаётся
/// (NextRetryDelay никогда не возвращает null — стандартная политика SignalR из коробки
/// останавливается после нескольких попыток, нам это не подходит для мобильного клиента,
/// который может надолго уходить в фон/терять сеть). Экспоненциальный бэкофф с потолком,
/// оба числа — из RealtimeConfig, не хардкод.
/// </summary>
public sealed class InfiniteRetryPolicy : IRetryPolicy
{
    private readonly RealtimeConfig mConfig;

    public InfiniteRetryPolicy(RealtimeConfig config)
    {
        mConfig = config;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        var delaySeconds = mConfig.InitialRetryDelaySeconds * Math.Pow(2, retryContext.PreviousRetryCount);
        delaySeconds = Math.Min(delaySeconds, mConfig.MaxRetryDelaySeconds);
        return TimeSpan.FromSeconds(delaySeconds);
    }
}
