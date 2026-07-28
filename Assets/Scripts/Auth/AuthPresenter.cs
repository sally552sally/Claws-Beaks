using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

/// <summary>
/// Единственный Presenter для Auth-сцены.
/// Управляет режимом (вход / регистрация) и выдаёт реактивные тексты.
/// View не знает о режиме — просто подписывается на тексты кнопок.
///
/// DEV_BUILD: автологин по DevCredentials при КАЖДОМ попадании на эту сцену — не только
/// при старте приложения через BootstrapEntryPoint, но и при редиректе сюда из середины
/// игры (SessionExpired → AppController → Auth). Раньше автологин срабатывал только в
/// Bootstrap, поэтому протухшая сессия посреди игры кидала на пустую форму логина вместо
/// того, чтобы тут же перелогиниться под тот же дев-аккаунт.
/// </summary>
public class AuthPresenter : DisposableObject, IInitializable
{
    // ─── Состояние ───────────────────────────────────────────────────────────

    private readonly Reactive<bool>   mIsLoginMode  = new(true);
    private readonly Reactive<bool>   mIsLoading    = new(false);
    private readonly Reactive<string> mBanMessage   = new(null);
    private readonly CancellationTokenSource mLifetimeCts = new();

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

    // ─── IInitializable ─────────────────────────────────────────────────────

    public void Initialize()
    {
#if DEV_BUILD
        // Автологин дев-аккаунтом при КАЖДОМ попадании на Auth-сцену — форма логина
        // в DEV_BUILD руками не заполняется вообще, см. класс-комментарий.
        mIsLoginMode.Value = true;
        SubmitAsync(DevCredentials.EMAIL, DevCredentials.PASSWORD, mLifetimeCts.Token).Forget();
#endif
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
        catch (ApiException ex)
        {
            if (ex.StatusCode == 403)
                mBanMessage.Value = ex.ServerError;
            else
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

    protected override void OnDispose()
    {
        mLifetimeCts.Cancel();
        mLifetimeCts.Dispose();
        base.OnDispose();
    }
}
