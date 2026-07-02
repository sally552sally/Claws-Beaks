using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Единственный Presenter для Auth-сцены.
/// Управляет режимом (вход / регистрация) и выдаёт реактивные тексты.
/// View не знает о режиме — просто подписывается на тексты кнопок.
/// </summary>
public class AuthPresenter : DisposableObject
{
    // ─── Состояние ───────────────────────────────────────────────────────────

    private readonly Reactive<bool>   mIsLoginMode  = new(true);
    private readonly Reactive<bool>   mIsLoading    = new(false);
    private readonly Reactive<string> mBanMessage   = new(null);

    public ReadonlyReactive<bool>   IsLoading    => mIsLoading.Readonly;
    public ReadonlyReactive<string> BanMessage   => mBanMessage.Readonly;

    // ─── Производные тексты (меняются при смене режима) ─────────────────────

    public readonly ReadonlyReactive<string> TitleText;
    public readonly ReadonlyReactive<string> SubmitButtonText;
    public readonly ReadonlyReactive<string> SwitchButtonText;

    // ─── Зависимости ─────────────────────────────────────────────────────────

    private readonly IAuthService mAuthService;
    private readonly ISceneLoader mSceneLoader;
    private readonly INotificationService mNotifications;

    [Inject]
    public AuthPresenter(IAuthService authService, ISceneLoader sceneLoader,
        INotificationService notifications)
    {
        mAuthService = authService;
        mSceneLoader = sceneLoader;
        mNotifications = notifications;

        TitleText        = mIsLoginMode.Readonly.Select(l => l ? "Вход"                        : "Регистрация");
        SubmitButtonText = mIsLoginMode.Readonly.Select(l => l ? "Войти"                        : "Зарегистрироваться");
        SwitchButtonText = mIsLoginMode.Readonly.Select(l => l ? "Нет аккаунта? Регистрация"   : "Уже есть аккаунт? Войти");

        // Все owned Reactive-объекты уничтожаются вместе с Presenter
        AutoDispose(mIsLoginMode, mIsLoading, mBanMessage);
    }

    // ─── Команды ─────────────────────────────────────────────────────────────

    /// <summary>Переключить режим вход ↔ регистрация. Очищает ошибки.</summary>
    public void SwitchMode()
    {
        mIsLoginMode.Value  = !mIsLoginMode.Value;
        mBanMessage.Value   = null;
    }

    /// <summary>Отправить форму — login или register в зависимости от текущего режима.</summary>
    public async UniTask SubmitAsync(string email, string password, CancellationToken ct)
    {
        if (!Validate(email, password)) return;

        mIsLoading.Value  = true;
        mBanMessage.Value = null;

        try
        {
            if (mIsLoginMode.Value)
                await mAuthService.LoginAsync(email, password, ct);
            else
                await mAuthService.RegisterAsync(email, password, ct);

            await mSceneLoader.LoadAsync(SceneNames.GAME, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 403)
        {
            mBanMessage.Value = ex.ServerError;
        }
        catch (ApiException ex)
        {
            mNotifications.ShowError(ex.ServerError);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mNotifications.ShowError("Нет подключения к серверу");
            Debug.LogError($"[AuthPresenter] Неожиданная ошибка: {ex}");
        }
        finally
        {
            if (!IsDisposed)
                mIsLoading.Value = false;
        }
    }

    // ─── Валидация (только для UX, сервер всё равно проверяет) ──────────────

    private bool Validate(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            mNotifications.ShowError("Введите email");
            return false;
        }

        if (!email.Contains("@"))
        {
            mNotifications.ShowError("Некорректный email");
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            mNotifications.ShowError("Введите пароль");
            return false;
        }

        if (password.Length < 8)
        {
            mNotifications.ShowError("Пароль не менее 8 символов");
            return false;
        }

        return true;
    }
}
