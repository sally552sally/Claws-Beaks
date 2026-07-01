using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Окно управления ботом: «MMORPG → Bot».
///
/// Как работает:
///   1) Ты входишь в Play Mode сам (Bootstrap → DEV-автологин → Game-сцена).
///      Бот НЕ входит в Play Mode за тебя намеренно: вход триггерит domain reload,
///      который стёр бы состояние окна прямо посреди запуска.
///   2) Выбираешь сценарий (список строится рефлексией по BotScenarios).
///   3) Start — бот резолвит живые презенторы из SceneContext и гоняет сценарий
///      через реальные команды презенторов (путь View→Presenter→Service→сервер живой).
///   4) Stop/Pause — отмена/пауза. Выход из Play Mode останавливает бота автоматически.
/// </summary>
public sealed class BotWindow : EditorWindow
{
    private readonly BotRunner mRunner = new();
    private readonly BotLog mLog = new();
    private readonly BotStats mStats = new();

    private List<(string name, MethodInfo method)> mScenarios = new();
    private string[] mScenarioNames = Array.Empty<string>();
    private int mSelectedScenario;

    private CancellationTokenSource mCts;
    private volatile bool mPaused;
    private bool mRunning;

    private Vector2 mLogScroll;

    [MenuItem("MMORPG/Bot")]
    public static void Open()
    {
        var window = GetWindow<BotWindow>();
        window.titleContent = new GUIContent("MMORPG Bot");
        window.minSize = new Vector2(420, 480);
        window.Show();
    }

    private void OnEnable()
    {
        ReloadScenarios();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        StopInternal("Окно закрыто");
    }

    // Пока бот работает — перерисовываем ~10 раз/сек, чтобы лог/таймеры обновлялись.
    private void OnInspectorUpdate()
    {
        if (mRunning || mLog.Dirty) Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(6);
        DrawControls();
        EditorGUILayout.Space(6);
        DrawStats();
        EditorGUILayout.Space(6);
        DrawLog();
    }

    // ─── Секции UI ──────────────────────────────────────────────────────────────

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Бот автопрогона", EditorStyles.boldLabel);

        bool playing = EditorApplication.isPlaying;
        bool ready = playing && BotGameAccess.IsGameReady();

        if (!playing)
        {
            EditorGUILayout.HelpBox(
                "Не в Play Mode. Нажми Play (Bootstrap → Game-сцена), потом Start.\n" +
                "Бот работает только по живой игре.",
                MessageType.Info);
        }
        else if (!ready)
        {
            EditorGUILayout.HelpBox(
                "Play Mode есть, но Game-сцена ещё не поднялась (или активна Auth/Bootstrap). " +
                "Дождись загрузки.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("Игра готова. Можно запускать сценарий.", MessageType.None);
        }
    }

    private void DrawControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(mRunning))
            {
                mSelectedScenario = EditorGUILayout.Popup(mSelectedScenario, mScenarioNames);
                if (GUILayout.Button("⟳", GUILayout.Width(28)))
                    ReloadScenarios();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            bool canStart = !mRunning
                            && EditorApplication.isPlaying
                            && BotGameAccess.IsGameReady()
                            && mScenarios.Count > 0;

            using (new EditorGUI.DisabledScope(!canStart))
            {
                if (GUILayout.Button("▶ Start", GUILayout.Height(28)))
                    StartRun();
            }

            using (new EditorGUI.DisabledScope(!mRunning))
            {
                if (GUILayout.Button("■ Stop", GUILayout.Height(28)))
                    StopInternal("Нажат Stop");

                bool paused = GUILayout.Toggle(mPaused, mPaused ? "▶ Resume" : "⏸ Pause",
                    "Button", GUILayout.Height(28), GUILayout.Width(90));
                mPaused = paused;
            }
        }

        if (mScenarios.Count == 0)
            EditorGUILayout.HelpBox("Сценарии не найдены. Добавь public static метод, " +
                                    "возвращающий BotScenario, в BotScenarios.cs.", MessageType.Warning);
    }

    private void DrawStats()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Статистика", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(mStats.Summary(), EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void DrawLog()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Лог", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Очистить", EditorStyles.miniButton, GUILayout.Width(80)))
                mLog.Clear();
        }

        mLogScroll = EditorGUILayout.BeginScrollView(mLogScroll,
            EditorStyles.helpBox, GUILayout.ExpandHeight(true));

        // Показываем хвост лога (последние строки — самые свежие внизу).
        foreach (var line in mLog.Lines)
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.EndScrollView();
        mLog.ClearDirty();

        // Автопрокрутка вниз, пока идёт прогон.
        if (mRunning) mLogScroll.y = float.MaxValue;
    }

    // ─── Логика запуска ──────────────────────────────────────────────────────────

    private void ReloadScenarios()
    {
        mScenarios = typeof(BotScenarios)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(BotScenario) && m.GetParameters().Length == 0)
            .Select(m => (m.Name, m))
            .ToList();

        mScenarioNames = mScenarios.Select(s => s.name).ToArray();
        if (mSelectedScenario >= mScenarios.Count) mSelectedScenario = 0;
    }

    private void StartRun()
    {
        mLog.Clear();
        mStats.Reset();

        if (!BotGameAccess.TryCreate(mLog, mStats, CancellationToken.None, out _, out var error))
        {
            mLog.Error(error);
            return;
        }

        BotScenario scenario;
        try
        {
            scenario = (BotScenario)mScenarios[mSelectedScenario].method.Invoke(null, null);
        }
        catch (Exception ex)
        {
            mLog.Error($"Не удалось собрать сценарий: {ex.InnerException?.Message ?? ex.Message}");
            return;
        }

        mCts = new CancellationTokenSource();
        mPaused = false;
        mRunning = true;

        // Пересобираем контекст с реальным токеном отмены.
        if (!BotGameAccess.TryCreate(mLog, mStats, mCts.Token, out var ctx, out var err2))
        {
            mLog.Error(err2);
            mRunning = false;
            return;
        }

        RunWrapper(scenario, ctx).Forget();
    }

    private async UniTaskVoid RunWrapper(BotScenario scenario, BotContext ctx)
    {
        try
        {
            await mRunner.RunAsync(scenario, ctx, () => mPaused);
        }
        catch (OperationCanceledException)
        {
            mLog.Info("⏹ Остановлено.");
        }
        catch (Exception ex)
        {
            mLog.Error($"Прогон упал: {ex.Message}");
        }
        finally
        {
            mRunning = false;
            mPaused = false;
            Repaint();
        }
    }

    private void StopInternal(string reason)
    {
        if (mCts != null && !mCts.IsCancellationRequested)
        {
            mLog.Info($"Останавливаю: {reason}");
            mCts.Cancel();
        }
        mRunning = false;
    }

    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        // Выход из Play Mode = игры больше нет, бота надо гасить.
        if (change == PlayModeStateChange.ExitingPlayMode)
            StopInternal("Выход из Play Mode");
    }
}
