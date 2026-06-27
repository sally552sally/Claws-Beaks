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
    private readonly Reactive<string> mErrorMessage = new(string.Empty);
    private readonly Reactive<string> mBanMessage   = new(null);

    public ReadonlyReactive<bool>   IsLoading    => mIsLoading.Readonly;
    public ReadonlyReactive<string> ErrorMessage => mErrorMessage.Readonly;
    public ReadonlyReactive<string> BanMessage   => mBanMessage.Readonly;

    // ─── Производные тексты (меняются при смене режима) ─────────────────────

    public readonly ReadonlyReactive<string> TitleText;
    public readonly ReadonlyReactive<string> SubmitButtonText;
    public readonly ReadonlyReactive<string> SwitchButtonText;

    // ─── Зависимости ─────────────────────────────────────────────────────────

    private readonly IAuthService mAuthService;
    private readonly ISceneLoader mSceneLoader;

    [Inject]
    public AuthPresenter(IAuthService authService, ISceneLoader sceneLoader)
    {
        mAuthService = authService;
        mSceneLoader = sceneLoader;

        TitleText        = mIsLoginMode.Readonly.Select(l => l ? "Вход"                        : "Регистрация");
        SubmitButtonText = mIsLoginMode.Readonly.Select(l => l ? "Войти"                        : "Зарегистрироваться");
        SwitchButtonText = mIsLoginMode.Readonly.Select(l => l ? "Нет аккаунта? Регистрация"   : "Уже есть аккаунт? Войти");

        // Все owned Reactive-объекты уничтожаются вместе с Presenter
        AutoDispose(mIsLoginMode, mIsLoading, mErrorMessage, mBanMessage);
    }

    // ─── Команды ─────────────────────────────────────────────────────────────

    /// <summary>Переключить режим вход ↔ регистрация. Очищает ошибки.</summary>
    public void SwitchMode()
    {
        mIsLoginMode.Value  = !mIsLoginMode.Value;
        mErrorMessage.Value = string.Empty;
        mBanMessage.Value   = null;
    }

    /// <summary>Отправить форму — login или register в зависимости от текущего режима.</summary>
    public async UniTask SubmitAsync(string email, string password, CancellationToken ct)
    {
        if (!Validate(email, password)) return;

        mIsLoading.Value    = true;
        mErrorMessage.Value = string.Empty;
        mBanMessage.Value   = null;

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
            mErrorMessage.Value = ex.ServerError;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            mErrorMessage.Value = "Нет подключения к серверу";
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
            mErrorMessage.Value = "Введите email";
            return false;
        }

        if (!email.Contains("@"))
        {
            mErrorMessage.Value = "Некорректный email";
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            mErrorMessage.Value = "Введите пароль";
            return false;
        }

        if (password.Length < 8)
        {
            mErrorMessage.Value = "Пароль не менее 8 символов";
            return false;
        }

        return true;
    }
}
