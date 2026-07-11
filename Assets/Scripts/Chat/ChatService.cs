using System.Threading;
using Cysharp.Threading.Tasks;
using Zenject;

/// <inheritdoc cref="IChatService" />
public sealed class ChatService : IChatService
{
    private readonly IApiClient mApiClient;

    [Inject]
    public ChatService(IApiClient apiClient)
    {
        mApiClient = apiClient;
    }

    public async UniTask<ChatMessageView> SendAsync(SendMessageRequest request, CancellationToken ct = default)
    {
        var response = await mApiClient.PostAsync<SendMessageResponse>("/api/chat/send", request, ct);
        return response.Message;
    }
}
