using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <inheritdoc cref="IChatHistoryService" />
public sealed class ChatHistoryService : DisposableObject, IChatHistoryService, IInitializable
{
    private readonly ChatConfig mConfig;
    private readonly CancellationTokenSource mLifetimeCts = new();

    /// <summary>Дедуп по Id — REST-эхо отправки и SignalR-пуш того же сообщения не должны
    /// попасть в буфер дважды. Перестраивается целиком при эвикции (см. EvictAndPublish).</summary>
    private readonly HashSet<long> mKnownIds = new();

    /// <summary>Рабочий список — НЕ гарантированно отсортирован между вызовами (сортируем
    /// заново в EvictAndPublish перед публикацией и эвикцией по количеству).</summary>
    private readonly List<ChatMessageView> mMessages = new();

    private readonly Reactive<List<ChatMessageView>> mAllMessages = new(new List<ChatMessageView>());
    public ReadonlyReactive<List<ChatMessageView>> AllMessages => mAllMessages.Readonly;

    [Inject]
    public ChatHistoryService(ChatConfig config)
    {
        mConfig = config;
        AutoDispose(mAllMessages);
    }

    // ─── IInitializable ─────────────────────────────────────────────────────

    public void Initialize()
    {
        CleanupLoopAsync(mLifetimeCts.Token).Forget();
    }

    // ─── Публичные команды ──────────────────────────────────────────────────

    public void AddMessage(ChatMessageView message)
    {
        if (message == null) return;
        if (!mKnownIds.Add(message.Id)) return; // уже есть — дедуп, см. класс-комментарий

        mMessages.Add(message);
        EvictAndPublish();
    }

    public void ClearLocationBuffer()
    {
        if (mConfig.PreserveLocationBufferOnLocationChange) return; // заглушка на будущее

        var removed = mMessages.RemoveAll(m => m.ChannelType == ChatChannelTypes.VIEW_LOCATION);
        if (removed == 0) return;

        EvictAndPublish();
    }

    // ─── Внутреннее ─────────────────────────────────────────────────────────

    private async UniTask CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(mConfig.CleanupIntervalSeconds), cancellationToken: ct);
            if (IsDisposed) return;

            // Чистит протухшие по времени сообщения, даже если новых не приходило —
            // иначе окно "последние 10 минут" молча растягивается в тишине канала.
            EvictAndPublish();
        }
    }

    /// <summary>Сортирует по времени, режет протухшее (по времени и по количеству),
    /// перестраивает индекс дедупа, публикует новый снимок в AllMessages.</summary>
    private void EvictAndPublish()
    {
        mMessages.Sort((a, b) => a.SentAt.CompareTo(b.SentAt));

        var cutoffUtc = DateTime.UtcNow.AddMinutes(-mConfig.BufferWindowMinutes);
        mMessages.RemoveAll(m => m.SentAt < cutoffUtc);

        if (mMessages.Count > mConfig.BufferMaxMessages)
            mMessages.RemoveRange(0, mMessages.Count - mConfig.BufferMaxMessages); // старейшие — после сортировки выше они первые

        mKnownIds.Clear();
        foreach (var m in mMessages)
            mKnownIds.Add(m.Id);

        if (IsDisposed) return;
        mAllMessages.Value = new List<ChatMessageView>(mMessages); // копия — не отдаём внутренний список на мутацию
    }

    protected override void OnDispose()
    {
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }
}
