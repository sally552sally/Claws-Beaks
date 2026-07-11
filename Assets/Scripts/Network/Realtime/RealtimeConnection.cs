using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

/// <summary>
/// Обёртка над одним SignalR-соединением (HubConnection): установка, переподключение
/// с бэкоффом (InfiniteRetryPolicy), кеш access-токена.
///
/// ПОТОКОБЕЗОПАСНОСТЬ (важно):
///   — Колбэки HubConnection (Reconnecting/Reconnected/Closed, On&lt;T&gt; обработчики)
///     вызываются SignalR из ФОНОВОГО потока, не из главного потока Unity.
///   — AccessTokenProvider тоже дёргается из фонового потока при авто-реконнекте —
///     PlayerPrefs (источник токена в ITokenStorage) трогать оттуда нельзя.
///     Поэтому токен кешируется в mCachedAccessToken и обновляется ТОЛЬКО с главного
///     потока (RefreshCachedToken), а сам провайдер лишь читает кеш.
///   — RegisterHandler переносит вызов handler на главный поток (UniTask.SwitchToMainThread)
///     ПЕРЕД вызовом — трогать Reactive/UI из обработчиков безопасно.
///   — Reconnecting/Reconnected/Closed внутри тоже переносятся на главный поток первым
///     делом — всё, что происходит дальше по цепочке (Resynced → RefreshAsync → Reactive),
///     уже гарантированно в главном потоке.
///
/// НЕ пытается быть универсальным RPC-фасадом: исходящие вызовы (JoinLocation и т.п.)
/// делай через Connection.InvokeAsync/SendAsync напрямую — к моменту, когда вызывающий
/// код запущен из RegisterHandler-колбэка или после Resynced, он уже на главном потоке.
///
/// Переиспользуется по экземпляру на каждый хаб (см. LocationRealtimeService;
/// ChatHub — по тому же паттерну, Фаза 2).
/// </summary>
public sealed class RealtimeConnection : DisposableObject
{
    private readonly ITokenStorage mTokenStorage;
    private readonly RealtimeConfig mConfig;
    private readonly string mHubPath;

    private readonly Reactive<HubConnectionState> mState = new(HubConnectionState.Disconnected);

    /// <summary>Кеш access-токена для AccessTokenProvider. Пишется ТОЛЬКО с главного потока
    /// (см. RefreshCachedToken), читается SignalR из фонового — volatile обязателен.</summary>
    private volatile string mCachedAccessToken;

    public ReadonlyReactive<HubConnectionState> State => mState.Readonly;

    /// <summary>Живой HubConnection — для RegisterHandler и исходящих InvokeAsync/SendAsync.</summary>
    public HubConnection Connection { get; }

    /// <summary>
    /// Первое успешное подключение ИЛИ восстановление после разрыва — сигнал владельцу,
    /// что нужен полный REST-ресинк (сервер не хранит буфер пропущенных за разрыв дельт).
    /// Вызывается уже в главном потоке.
    /// </summary>
    public event Action Resynced;

    public RealtimeConnection(ApiConfig apiConfig, ITokenStorage tokenStorage, RealtimeConfig config, string hubPath)
    {
        mTokenStorage = tokenStorage;
        mConfig = config;
        mHubPath = hubPath;

        AutoDispose(mState);

        RefreshCachedToken(); // конструктор вызывается на главном потоке (Zenject) — безопасно

        Connection = new HubConnectionBuilder()
            .WithUrl(apiConfig.BaseUrl + mHubPath, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(mCachedAccessToken);
            })
            .AddNewtonsoftJsonProtocol()
            .WithAutomaticReconnect(new InfiniteRetryPolicy(mConfig))
            .Build();

        Connection.Reconnecting += OnReconnecting;
        Connection.Reconnected += OnReconnected;
        Connection.Closed += OnClosed;
    }

    /// <summary>
    /// Устанавливает соединение. При неудаче ретраит с растущей паузой (RealtimeConfig),
    /// пока не получится или не отменят через ct. Покрывает только ПЕРВОЕ подключение —
    /// после него разрывы обрабатывает встроенный WithAutomaticReconnect.
    /// </summary>
    public async UniTask StartAsync(CancellationToken ct)
    {
        var delaySeconds = (double)mConfig.InitialRetryDelaySeconds;

        while (!ct.IsCancellationRequested && !IsDisposed)
        {
            RefreshCachedToken();
            mState.Value = HubConnectionState.Connecting;

            try
            {
                await Connection.StartAsync(ct);
                if (IsDisposed) return;
                mState.Value = HubConnectionState.Connected;
                Debug.Log($"[RealtimeConnection] {mHubPath}: подключено (ConnectionId={Connection.ConnectionId}).");
                Resynced?.Invoke();
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                Debug.LogWarning($"[RealtimeConnection] {mHubPath}: не удалось подключиться " +
                                  $"({ex.Message}). Повтор через {delaySeconds:0}с.");
                mState.Value = HubConnectionState.Disconnected;
                await UniTask.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken: ct);
                delaySeconds = Math.Min(delaySeconds * 2, mConfig.MaxRetryDelaySeconds);
            }
        }
    }

    /// <summary>Останавливает соединение. Вызывается из OnDispose при уничтожении владельца.</summary>
    public async UniTask StopAsync()
    {
        try
        {
            await Connection.StopAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RealtimeConnection] {mHubPath}: ошибка остановки ({ex.Message}).");
        }
    }

    /// <summary>
    /// Подписывает обработчик на серверный метод хаба. Сам колбэк SignalR приходит из
    /// фонового потока — здесь он гарантированно переносится на главный поток Unity
    /// перед вызовом handler, чтобы тот мог безопасно трогать Reactive/UI.
    /// </summary>
    public IDisposable RegisterHandler<T>(string methodName, Action<T> handler)
    {
        return Connection.On<T>(methodName, async (T arg) =>
        {
            await UniTask.SwitchToMainThread();
            if (IsDisposed) return;
            handler(arg);
        });
    }

    // ─── Обработчики HubConnection (вызываются SignalR из фонового потока) ──────

    private async Task OnReconnecting(Exception ex)
    {
        await UniTask.SwitchToMainThread();
        if (IsDisposed) return;
        RefreshCachedToken(); // до следующей попытки — вдруг токен успел обновиться
        mState.Value = HubConnectionState.Reconnecting;
        Debug.Log($"[RealtimeConnection] {mHubPath}: переподключение ({ex?.Message}).");
    }

    private async Task OnReconnected(string connectionId)
    {
        await UniTask.SwitchToMainThread();
        if (IsDisposed) return;
        mState.Value = HubConnectionState.Connected;
        Debug.Log($"[RealtimeConnection] {mHubPath}: переподключено (ConnectionId={connectionId}).");
        Resynced?.Invoke();
    }

    private async Task OnClosed(Exception ex)
    {
        await UniTask.SwitchToMainThread();
        if (IsDisposed) return;
        // При бесконечной retry-политике (InfiniteRetryPolicy) сюда попадаем практически
        // только при явном StopAsync() — уничтожение сервиса при смене сцены/логауте.
        // Сами повторный StartAsync не запускаем — это ответственность владельца.
        mState.Value = HubConnectionState.Disconnected;
        Debug.Log($"[RealtimeConnection] {mHubPath}: соединение закрыто ({ex?.Message}).");
    }

    // ─── Внутреннее ──────────────────────────────────────────────────────────

    /// <summary>Обновляет кеш токена для AccessTokenProvider. Вызывать ТОЛЬКО с главного потока.</summary>
    private void RefreshCachedToken() => mCachedAccessToken = mTokenStorage.GetAccessToken();

    protected override void OnDispose()
    {
        Connection.Reconnecting -= OnReconnecting;
        Connection.Reconnected -= OnReconnected;
        Connection.Closed -= OnClosed;
        StopAsync().Forget();
        base.OnDispose();
    }
}
