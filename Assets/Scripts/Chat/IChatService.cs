using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>REST-отправка сообщений чата (POST /api/chat/send).</summary>
public interface IChatService
{
    /// <summary>Отправляет сообщение. Бросает ApiException с человекочитаемым текстом
    /// сервера (рейт-лимит / мут / лимит длины / блок и т.д. — см. ChatService.SendAsync
    /// на сервере, все тексты уже готовы для показа игроку как есть).</summary>
    UniTask<ChatMessageView> SendAsync(SendMessageRequest request, CancellationToken ct = default);
}
