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
///   2) Выбираешь сценарий (рефлексия по BotScenarios) и правишь его параметры
///      прямо в окне (локации/сеты — выпадашками с реальными данными игры).
///   3) «Проверить» — сухой прогон: ловит опечатки/недостижимые пути/отсутствующие
///      сеты ДО запуска. «Start» — запуск; Stop/Pause — управление.
///   4) Оверлей поверх игры показывает текущий шаг; окно — прогресс, статы, лог
///      с фильтрами по каналам и экспортом в файл (BotRuns/ в корне проекта).
/// </summary>
public sealed class BotWindow : EditorWindow
{
    private const string PREFS_PREFIX = "MMORPGBot.";

    private readonly BotRunner mRunner = new();
    private readonly BotLog mLog = new();
    private readonly BotStats mStats = new();
    private readonly BotOptions mOptions = new();

    // Сценарии (рефлексия).
    private List<(string name, MethodInfo method, bool hasParams)> mScenarios = new();
    private string[] mScenarioNames = Array.Empty<string>();
    private int mSelectedScenario;

    // Параметры выбранного сценария.
    private IReadOnlyList<BotParamSpec> mParamSpecs = Array.Empty<BotParamSpec>();
    private readonly Dictionary<string, object> mParamValues = new();

    // Кеш данных игры для выпадашек.
    private string[] mLocCodes = Array.Empty<string>();
    private string[] mLocDisplay = Array.Empty<string>();
    private long[] mSetIds = Array.Empty<long>();
    private string[] mSetIdDisplay = Array.Empty<string>();
    private bool mFetchingData;
    private bool mAutoFetched;

    // Состояние прогона.
    private CancellationTokenSource mCts;
    private volatile bool mPaused;
    private bool mRunning;
    private bool mBusyDryRun;
    private BotContext mActiveCtx;

    // Ручной режим.
    private int mManualLocIndex;
    private int mManualMobs = 1;
    private long mManualSetId = 1;

    // UI-состояние.
    private Vector2 mLogScroll;
    private bool mShowOptions = true;
    private bool mShowProgress = true;
    private bool mShowManual;
    private bool mShowTimings;
    private readonly bool[] mChannelOn = { true, true, true, true, true }; // System/Combat/Inventory/Navigation/Assert
    private bool mOnlyProblems;
    private string mSearch = "";

    [MenuItem("MMORPG/Bot")]
    public static void Open()
    {
        var window = GetWindow<BotWindow>();
        window.titleContent = new GUIContent("MMORPG Bot");
        window.minSize = new Vector2(460, 560);
        window.Show();
    }

    private void OnEnable()
    {
        LoadOptions();
        ReloadScenarios();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        StopInternal("Окно закрыто");
        BotOverlay.Hide();
    }

    // Пока бот работает — перерисовываем ~10 раз/сек + обновляем оверлей.
    private void OnInspectorUpdate()
    {
        if (mRunning && mActiveCtx != null && mOptions.ShowOverlay && Application.isPlaying)
        {
            BotOverlay.Show();
            BotOverlay.SetText(mActiveCtx.Progress.OverlayText);
        }
        else
        {
            BotOverlay.Hide();
        }

        if (mRunning || mLog.Dirty) Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();
        EditorGUILayout.Space(4);
        DrawScenarioAndParams();
        EditorGUILayout.Space(4);
        DrawControls();
        EditorGUILayout.Space(4);
        DrawOptions();
        DrawProgress();
        DrawManual();
        DrawStats();
        EditorGUILayout.Space(4);
        DrawLog();
    }

    // ─── Шапка ───────────────────────────────────────────────────────────────

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
            mAutoFetched = false;
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

            // Автоподтяжка данных игры (локации/сеты) один раз за сессию Play Mode.
            if (!mAutoFetched && !mFetchingData)
            {
                mAutoFetched = true;
                FetchGameDataAsync().Forget();
            }
        }
    }

    // ─── Сценарий + параметры ────────────────────────────────────────────────

    private void DrawScenarioAndParams()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(mRunning))
            {
                var newSelected = EditorGUILayout.Popup(mSelectedScenario, mScenarioNames);
                if (newSelected != mSelectedScenario)
                {
                    mSelectedScenario = newSelected;
                    CollectParamsForSelected();
                }

                if (GUILayout.Button("⟳ сценарии", GUILayout.Width(90)))
                    ReloadScenarios();

                using (new EditorGUI.DisabledScope(mFetchingData || !BotGameAccess.IsGameReady()))
                {
                    if (GUILayout.Button(mFetchingData ? "…" : "⟳ данные игры", GUILayout.Width(100)))
                        FetchGameDataAsync().Forget();
                }
            }
        }

        if (mScenarios.Count == 0)
        {
            EditorGUILayout.HelpBox("Сценарии не найдены. Добавь public static метод, " +
                                    "возвращающий BotScenario, в BotScenarios.cs.", MessageType.Warning);
            return;
        }

        // Поля параметров выбранного сценария.
        if (mParamSpecs.Count == 0) return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Параметры сценария", EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(mRunning))
            {
                foreach (var spec in mParamSpecs)
                    DrawParamField(spec);
            }
            if (EditorGUI.EndChangeCheck())
                SaveParamValues();
        }
    }

    // ─── Конвертация значений параметров (object из словаря → нужный тип поля) ──

    private static int ToInt(object value, BotParamSpec spec)
    {
        if (value is int i) return i;
        if (value != null && int.TryParse(value.ToString(), out var parsed)) return parsed;
        return spec.DefaultValue is int def ? def : 0;
    }

    private static float ToFloat(object value, BotParamSpec spec)
    {
        if (value is float f) return f;
        if (value != null && float.TryParse(value.ToString(), out var parsed)) return parsed;
        return spec.DefaultValue is float def ? def : 0f;
    }

    private static bool ToBool(object value, BotParamSpec spec)
    {
        if (value is bool b) return b;
        if (value != null && bool.TryParse(value.ToString(), out var parsed)) return parsed;
        return spec.DefaultValue is bool def && def;
    }

    private static string ToText(object value, BotParamSpec spec)
    {
        if (value is string s) return s;
        if (value != null) return value.ToString();
        return spec.DefaultValue as string ?? "";
    }

    private static long ToLong(object value, BotParamSpec spec)
    {
        if (value is long l) return l;
        if (value != null && long.TryParse(value.ToString(), out var parsed)) return parsed;
        return spec.DefaultValue is long def ? def : 0L;
    }

    private void DrawParamField(BotParamSpec spec)
    {
        mParamValues.TryGetValue(spec.Name, out var value);

        switch (spec.Kind)
        {
            case BotParamKind.Int:
                mParamValues[spec.Name] = EditorGUILayout.IntField(spec.Name, ToInt(value, spec));
                break;

            case BotParamKind.Float:
                mParamValues[spec.Name] = EditorGUILayout.FloatField(spec.Name, ToFloat(value, spec));
                break;

            case BotParamKind.Bool:
                mParamValues[spec.Name] = EditorGUILayout.Toggle(spec.Name, ToBool(value, spec));
                break;

            case BotParamKind.Text:
                mParamValues[spec.Name] = EditorGUILayout.TextField(spec.Name, ToText(value, spec));
                break;

            case BotParamKind.Location:
                DrawLocationField(spec, ToText(value, spec));
                break;

            case BotParamKind.SetId:
                DrawSetIdField(spec, ToLong(value, spec));
                break;
        }
    }

    /// <summary>Локация: выпадашка реальных кодов; если кеша нет — текстовое поле.</summary>
    private void DrawLocationField(BotParamSpec spec, string current)
    {
        if (mLocCodes.Length == 0)
        {
            mParamValues[spec.Name] = EditorGUILayout.TextField($"{spec.Name} (код)", current);
            return;
        }

        int index = Array.IndexOf(mLocCodes, current);
        if (index < 0) index = 0;
        int newIndex = EditorGUILayout.Popup(spec.Name, index, mLocDisplay);
        mParamValues[spec.Name] = mLocCodes[newIndex];
    }

    /// <summary>SetId: выпадашка реальных сетов; если кеша нет — числовое поле.</summary>
    private void DrawSetIdField(BotParamSpec spec, long current)
    {
        if (mSetIds.Length == 0)
        {
            mParamValues[spec.Name] = EditorGUILayout.LongField(spec.Name, current);
            return;
        }

        int index = Array.IndexOf(mSetIds, current);
        if (index < 0) index = 0;
        int newIndex = EditorGUILayout.Popup(spec.Name, index, mSetIdDisplay);
        mParamValues[spec.Name] = mSetIds[newIndex];
    }

    // ─── Управление ──────────────────────────────────────────────────────────

    private void DrawControls()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            bool ready = EditorApplication.isPlaying && BotGameAccess.IsGameReady();
            bool canStart = !mRunning && !mBusyDryRun && ready && mScenarios.Count > 0;

            using (new EditorGUI.DisabledScope(!canStart))
            {
                if (GUILayout.Button("▶ Start", GUILayout.Height(28)))
                {
                    var scenario = BuildSelectedScenario();
                    if (scenario != null) StartScenario(scenario, resetAll: true);
                }

                if (GUILayout.Button("🔎 Проверить", GUILayout.Height(28), GUILayout.Width(110)))
                    StartDryRun();
            }

            using (new EditorGUI.DisabledScope(!mRunning))
            {
                if (GUILayout.Button("■ Stop", GUILayout.Height(28), GUILayout.Width(70)))
                    StopInternal("Нажат Stop");

                mPaused = GUILayout.Toggle(mPaused, mPaused ? "▶ Resume" : "⏸ Pause",
                    "Button", GUILayout.Height(28), GUILayout.Width(90));
            }
        }
    }

    // ─── Настройки ───────────────────────────────────────────────────────────

    private void DrawOptions()
    {
        mShowOptions = EditorGUILayout.Foldout(mShowOptions, "Настройки прогона", true);
        if (!mShowOptions) return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();

            mOptions.ActionDelaySeconds = EditorGUILayout.Slider(
                new GUIContent("Пауза после действия, с",
                    "0 = полная скорость. 1-2с — удобно следить глазами: пауза после каждого " +
                    "хода в бою, мутации инвентаря, перехода и шага сценария."),
                mOptions.ActionDelaySeconds, 0f, 10f);

            mOptions.StopAfterErrors = EditorGUILayout.IntField(
                new GUIContent("Стоп после N проблем", "Ошибки + проваленные проверки. 0 = не останавливать."),
                Mathf.Max(0, mOptions.StopAfterErrors));

            mOptions.StopOnDeath = EditorGUILayout.Toggle(
                new GUIContent("Стоп при смерти", "Остановить прогон, если персонаж погиб."),
                mOptions.StopOnDeath);

            mOptions.ScreenshotOnError = EditorGUILayout.Toggle(
                new GUIContent("Скриншот при проблеме", "PNG в BotRuns/screens/ при ошибке шага или проваленной проверке."),
                mOptions.ScreenshotOnError);

            mOptions.ShowOverlay = EditorGUILayout.Toggle(
                new GUIContent("Оверлей в игре", "Плашка «что делает бот» поверх экрана игры."),
                mOptions.ShowOverlay);

            mOptions.AutoExportOnFinish = EditorGUILayout.Toggle(
                new GUIContent("Автоэкспорт лога", "По окончании прогона сохранить лог+статы в BotRuns/*.log."),
                mOptions.AutoExportOnFinish);

            if (EditorGUI.EndChangeCheck())
                SaveOptions();
        }
    }

    // ─── Прогресс ────────────────────────────────────────────────────────────

    private void DrawProgress()
    {
        if (mActiveCtx == null) return;

        mShowProgress = EditorGUILayout.Foldout(mShowProgress, "Прогресс", true);
        if (!mShowProgress) return;

        var progress = mActiveCtx.Progress;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (progress.Loop)
                EditorGUILayout.LabelField($"Круг: {progress.Pass}", EditorStyles.miniBoldLabel);

            for (int i = 0; i < progress.StepTitles.Count; i++)
            {
                string marker = i < progress.CurrentIndex ? "✓"
                              : i == progress.CurrentIndex ? "▶"
                              : "○";
                string detail = i == progress.CurrentIndex && !string.IsNullOrEmpty(progress.Detail)
                    ? $"  ({progress.Detail})"
                    : "";
                var style = i == progress.CurrentIndex ? EditorStyles.boldLabel : EditorStyles.miniLabel;
                EditorGUILayout.LabelField($"{marker} {progress.StepTitles[i]}{detail}", style);
            }
        }
    }

    // ─── Ручной режим ────────────────────────────────────────────────────────

    private void DrawManual()
    {
        mShowManual = EditorGUILayout.Foldout(mShowManual, "Ручной режим (разовые команды)", true);
        if (!mShowManual) return;

        bool ready = EditorApplication.isPlaying && BotGameAccess.IsGameReady();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        using (new EditorGUI.DisabledScope(mRunning || mBusyDryRun || !ready))
        {
            // Идти в локацию.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (mLocCodes.Length > 0)
                {
                    mManualLocIndex = EditorGUILayout.Popup(mManualLocIndex, mLocDisplay);
                    if (GUILayout.Button("Идти", GUILayout.Width(80)))
                        RunAdhoc(new GoToStep(mLocCodes[Mathf.Clamp(mManualLocIndex, 0, mLocCodes.Length - 1)]));
                }
                else
                {
                    EditorGUILayout.LabelField("Локации не загружены («⟳ данные игры»)", EditorStyles.miniLabel);
                }
            }

            // Убить мобов.
            using (new EditorGUILayout.HorizontalScope())
            {
                mManualMobs = Mathf.Max(1, EditorGUILayout.IntField("Мобов", mManualMobs));
                if (GUILayout.Button("Убить", GUILayout.Width(80)))
                    RunAdhoc(new KillMobsStep(mManualMobs, null));
            }

            // Одеть сет / снять всё / снимок.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (mSetIds.Length > 0)
                {
                    int idx = Array.IndexOf(mSetIds, mManualSetId);
                    if (idx < 0) idx = 0;
                    idx = EditorGUILayout.Popup("Сет", idx, mSetIdDisplay);
                    mManualSetId = mSetIds[idx];
                }
                else
                {
                    mManualSetId = EditorGUILayout.LongField("Сет (SetId)", mManualSetId);
                }

                if (GUILayout.Button("Одеть", GUILayout.Width(80)))
                    RunAdhoc(new EquipSetStep(mManualSetId));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Снять всё"))
                    RunAdhoc(new UnequipAllStep());
                if (GUILayout.Button("Снимок в лог"))
                    RunAdhoc(new SnapshotStep("ручной"));
            }
        }
    }

    // ─── Статистика ──────────────────────────────────────────────────────────

    private void DrawStats()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Статистика", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(mStats.Summary(), EditorStyles.wordWrappedMiniLabel);

            mShowTimings = EditorGUILayout.Foldout(mShowTimings, "Тайминги шагов", true);
            if (mShowTimings)
                EditorGUILayout.LabelField(mStats.TimingsReport(), EditorStyles.wordWrappedMiniLabel);
        }
    }

    // ─── Лог ─────────────────────────────────────────────────────────────────

    private void DrawLog()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Лог", EditorStyles.miniBoldLabel, GUILayout.Width(30));

            // Фильтры по каналам.
            mChannelOn[0] = GUILayout.Toggle(mChannelOn[0], "Сис", EditorStyles.miniButtonLeft, GUILayout.Width(40));
            mChannelOn[1] = GUILayout.Toggle(mChannelOn[1], "Бой", EditorStyles.miniButtonMid, GUILayout.Width(40));
            mChannelOn[2] = GUILayout.Toggle(mChannelOn[2], "Инв", EditorStyles.miniButtonMid, GUILayout.Width(40));
            mChannelOn[3] = GUILayout.Toggle(mChannelOn[3], "Пер", EditorStyles.miniButtonMid, GUILayout.Width(40));
            mChannelOn[4] = GUILayout.Toggle(mChannelOn[4], "Пров", EditorStyles.miniButtonRight, GUILayout.Width(45));

            mOnlyProblems = GUILayout.Toggle(mOnlyProblems, "⚠ только проблемы", EditorStyles.miniButton, GUILayout.Width(120));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Экспорт", EditorStyles.miniButton, GUILayout.Width(60)))
                ExportLog();
            if (GUILayout.Button("Очистить", EditorStyles.miniButton, GUILayout.Width(65)))
                mLog.Clear();
        }

        mSearch = EditorGUILayout.TextField(mSearch, EditorStyles.toolbarSearchField);

        mLogScroll = EditorGUILayout.BeginScrollView(mLogScroll,
            EditorStyles.helpBox, GUILayout.ExpandHeight(true));

        // Фильтрация: каналы → уровень → поиск. Рисуем последние 300 после фильтра.
        var filtered = new List<BotLogEntry>();
        foreach (var entry in mLog.Entries)
        {
            if (!mChannelOn[(int)entry.Channel]) continue;
            if (mOnlyProblems && entry.Level != BotLogLevel.Warn && entry.Level != BotLogLevel.Error) continue;
            if (!string.IsNullOrEmpty(mSearch) &&
                entry.Message.IndexOf(mSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
            filtered.Add(entry);
        }

        int start = Mathf.Max(0, filtered.Count - 300);
        for (int i = start; i < filtered.Count; i++)
            EditorGUILayout.LabelField(filtered[i].Format(), EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.EndScrollView();
        mLog.ClearDirty();

        // Автопрокрутка вниз, пока идёт прогон.
        if (mRunning) mLogScroll.y = float.MaxValue;
    }

    // ─── Сценарии: рефлексия + параметры ─────────────────────────────────────

    private void ReloadScenarios()
    {
        mScenarios = typeof(BotScenarios)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(BotScenario))
            .Where(m =>
            {
                var pars = m.GetParameters();
                return pars.Length == 0 ||
                       (pars.Length == 1 && pars[0].ParameterType == typeof(BotParams));
            })
            .Select(m => (m.Name, m, m.GetParameters().Length == 1))
            .ToList();

        mScenarioNames = mScenarios.Select(s => s.Item1).ToArray();
        if (mSelectedScenario >= mScenarios.Count) mSelectedScenario = 0;

        CollectParamsForSelected();
    }

    /// <summary>Первый вызов метода сценария — в режиме Collector, чтобы узнать параметры.</summary>
    private void CollectParamsForSelected()
    {
        mParamSpecs = Array.Empty<BotParamSpec>();
        mParamValues.Clear();

        if (mScenarios.Count == 0 || mSelectedScenario >= mScenarios.Count) return;
        var (name, method, hasParams) = mScenarios[mSelectedScenario];
        if (!hasParams) return;

        try
        {
            var collector = BotParams.Collector();
            method.Invoke(null, new object[] { collector }); // результат-сценарий не нужен
            mParamSpecs = collector.Specs;

            // Загружаем сохранённые значения (или дефолты).
            foreach (var spec in mParamSpecs)
                mParamValues[spec.Name] = LoadParamValue(name, spec);
        }
        catch (Exception ex)
        {
            mLog.Error($"Не смог прочитать параметры «{name}»: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    /// <summary>Второй вызов — с реальными значениями из окна.</summary>
    private BotScenario BuildSelectedScenario()
    {
        if (mScenarios.Count == 0) return null;
        var (name, method, hasParams) = mScenarios[mSelectedScenario];

        try
        {
            return hasParams
                ? (BotScenario)method.Invoke(null, new object[] { BotParams.With(new Dictionary<string, object>(mParamValues)) })
                : (BotScenario)method.Invoke(null, null);
        }
        catch (Exception ex)
        {
            mLog.Error($"Не удалось собрать сценарий «{name}»: {ex.InnerException?.Message ?? ex.Message}");
            return null;
        }
    }

    // ─── Персист параметров и настроек (EditorPrefs переживает domain reload) ──

    private string ParamKey(string scenario, string param) => $"{PREFS_PREFIX}param.{scenario}.{param}";

    private object LoadParamValue(string scenario, BotParamSpec spec)
    {
        var key = ParamKey(scenario, spec.Name);
        if (!EditorPrefs.HasKey(key)) return spec.DefaultValue;

        var raw = EditorPrefs.GetString(key);
        try
        {
            return spec.Kind switch
            {
                BotParamKind.Int => int.Parse(raw),
                BotParamKind.Float => float.Parse(raw),
                BotParamKind.Bool => bool.Parse(raw),
                BotParamKind.SetId => long.Parse(raw),
                _ => raw
            };
        }
        catch { return spec.DefaultValue; }
    }

    private void SaveParamValues()
    {
        if (mScenarios.Count == 0) return;
        var scenario = mScenarios[mSelectedScenario].Item1;

        foreach (var kv in mParamValues)
            EditorPrefs.SetString(ParamKey(scenario, kv.Key), kv.Value?.ToString() ?? "");
    }

    private void LoadOptions()
    {
        mOptions.ActionDelaySeconds = EditorPrefs.GetFloat(PREFS_PREFIX + "opt.delay", 0f);
        mOptions.StopAfterErrors = EditorPrefs.GetInt(PREFS_PREFIX + "opt.stopErrors", 0);
        mOptions.StopOnDeath = EditorPrefs.GetBool(PREFS_PREFIX + "opt.stopDeath", false);
        mOptions.ScreenshotOnError = EditorPrefs.GetBool(PREFS_PREFIX + "opt.screenshot", true);
        mOptions.ShowOverlay = EditorPrefs.GetBool(PREFS_PREFIX + "opt.overlay", true);
        mOptions.AutoExportOnFinish = EditorPrefs.GetBool(PREFS_PREFIX + "opt.autoExport", true);
    }

    private void SaveOptions()
    {
        EditorPrefs.SetFloat(PREFS_PREFIX + "opt.delay", mOptions.ActionDelaySeconds);
        EditorPrefs.SetInt(PREFS_PREFIX + "opt.stopErrors", mOptions.StopAfterErrors);
        EditorPrefs.SetBool(PREFS_PREFIX + "opt.stopDeath", mOptions.StopOnDeath);
        EditorPrefs.SetBool(PREFS_PREFIX + "opt.screenshot", mOptions.ScreenshotOnError);
        EditorPrefs.SetBool(PREFS_PREFIX + "opt.overlay", mOptions.ShowOverlay);
        EditorPrefs.SetBool(PREFS_PREFIX + "opt.autoExport", mOptions.AutoExportOnFinish);
    }

    // ─── Данные игры для выпадашек ───────────────────────────────────────────

    private async UniTaskVoid FetchGameDataAsync()
    {
        mFetchingData = true;
        try
        {
            if (!BotGameAccess.TryGetServices(out var locationService, out var inventoryService))
            {
                mLog.Warn("Не смог достать сервисы игры для выпадашек.");
                return;
            }

            var map = await locationService.GetMapAsync(CancellationToken.None);
            mLocCodes = map.Locations.Select(l => l.Code).ToArray();
            mLocDisplay = map.Locations.Select(l => $"{l.Code} — {l.Name}").ToArray();

            var setIds = new HashSet<long>();
            void Collect(IEnumerable<InventoryItemDto> items)
            {
                if (items == null) return;
                foreach (var item in items)
                    if (item.SetId.HasValue) setIds.Add(item.SetId.Value);
            }

            var inventory = await inventoryService.GetInventoryAsync(CancellationToken.None);
            Collect(inventory.Backpack);
            Collect(inventory.Equipped);
            try
            {
                var chest = await inventoryService.GetChestAsync(CancellationToken.None);
                Collect(chest.Items);
            }
            catch { /* сундук может быть недоступен из текущей локации — не страшно */ }

            mSetIds = setIds.OrderBy(x => x).ToArray();
            mSetIdDisplay = mSetIds.Select(id => $"Сет #{id}").ToArray();

            mLog.Info($"Данные игры обновлены: локаций {mLocCodes.Length}, сетов {mSetIds.Length}.");
        }
        catch (Exception ex)
        {
            mLog.Warn($"Не смог получить данные игры: {ex.Message}");
        }
        finally
        {
            mFetchingData = false;
            Repaint();
        }
    }

    // ─── Запуск / остановка ──────────────────────────────────────────────────

    private void StartScenario(BotScenario scenario, bool resetAll)
    {
        if (resetAll)
        {
            mLog.Clear();
            mStats.Reset();
        }

        mCts = new CancellationTokenSource();
        mPaused = false;

        if (!BotGameAccess.TryCreate(mLog, mStats, mCts.Token, out var ctx, out var error))
        {
            mLog.Error(error);
            return;
        }

        ctx.Options = mOptions;
        mActiveCtx = ctx;
        mRunning = true;

        RunWrapper(scenario, ctx).Forget();
    }

    /// <summary>Разовая команда из ручного режима (стата копится, лог не чистим).</summary>
    private void RunAdhoc(IBotStep step)
    {
        var scenario = new BotScenario($"Ручное: {step.Describe}", new[] { step }, loop: false);
        StartScenario(scenario, resetAll: false);
    }

    private void StartDryRun()
    {
        var scenario = BuildSelectedScenario();
        if (scenario == null) return;

        if (!BotGameAccess.TryCreate(mLog, mStats, CancellationToken.None, out var ctx, out var error))
        {
            mLog.Error(error);
            return;
        }

        ctx.Options = mOptions;
        mBusyDryRun = true;
        DryRunWrapper(scenario, ctx).Forget();
    }

    private async UniTaskVoid DryRunWrapper(BotScenario scenario, BotContext ctx)
    {
        try { await BotDryRun.RunAsync(scenario, ctx); }
        catch (Exception ex) { mLog.Error($"Сухой прогон упал: {ex.Message}"); }
        finally
        {
            mBusyDryRun = false;
            Repaint();
        }
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
            BotOverlay.Hide();

            if (mOptions.AutoExportOnFinish)
                ExportLog();

            Repaint();
        }
    }

    private void ExportLog()
    {
        var name = mScenarios.Count > 0 ? mScenarios[mSelectedScenario].Item1 : "bot";
        var path = mLog.ExportToFile(name, mStats.Summary(), mStats.TimingsReport());
        if (path != null)
            mLog.Info($"💾 Лог сохранён: {path}");
    }

    private void StopInternal(string reason)
    {
        if (mCts != null && !mCts.IsCancellationRequested)
        {
            mLog.Info($"Останавливаю: {reason}");
            mCts.Cancel();
        }
        mRunning = false;
        BotOverlay.Hide();
    }

    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        // Выход из Play Mode = игры больше нет, бота надо гасить.
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            StopInternal("Выход из Play Mode");
            mAutoFetched = false;
        }
    }
}
