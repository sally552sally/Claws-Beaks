using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// ─── Запросы ──────────────────────────────────────────────────────────────────

/// <summary>Состояние одного участника боя (игрок или моб).</summary>
public sealed class CombatParticipantView
{
    [JsonProperty("participantId")]        public long    ParticipantId       { get; set; }
    [JsonProperty("name")]                 public string  Name                { get; set; }
    [JsonProperty("currentHp")]            public int     CurrentHp           { get; set; }
    [JsonProperty("maxHp")]               public int     MaxHp               { get; set; }
    [JsonProperty("side")]                 public string  Side                { get; set; }
    [JsonProperty("isAlive")]              public bool    IsAlive             { get; set; }
    [JsonProperty("isMob")]               public bool    IsMob               { get; set; }
    [JsonProperty("opponentParticipantId")] public long?  OpponentParticipantId { get; set; }

    /// <summary>
    /// Суммарный урон бойца за весь бой. Нужен таблице участников в окне результата
    /// (см. BattleReportPresenter): смысл окна не в награде, а в «кто был и кто отработал».
    /// Значение копилось на сервере с самого начала — наружу его стали отдавать вместе
    /// с этой задачей.
    /// </summary>
    [JsonProperty("damageDealt")]           public long   DamageDealt { get; set; }
}

/// <summary>Один удар в бою (используется в трейсе и комбо).</summary>
public sealed class CombatHitView
{
    [JsonProperty("participantId")]        public long    ParticipantId       { get; set; }
    [JsonProperty("targetParticipantId")]  public long    TargetParticipantId { get; set; }
    [JsonProperty("stance")]               public string  Stance              { get; set; }
    [JsonProperty("direction")]            public string  Direction           { get; set; }
    [JsonProperty("damage")]               public int     Damage              { get; set; }
    [JsonProperty("wasBlock")]             public bool    WasBlock            { get; set; }
    [JsonProperty("wasDodge")]             public bool    WasDodge            { get; set; }
    [JsonProperty("wasCrit")]              public bool    WasCrit             { get; set; }
    [JsonProperty("wasComboFinisher")]     public bool    WasComboFinisher    { get; set; }
    [JsonProperty("comboLevel")]           public int?    ComboLevel          { get; set; }
    [JsonProperty("targetHpAfter")]        public int     TargetHpAfter       { get; set; }

    /// <summary>
    /// Хил атакующему от финишера Vampirism (0 — не было). Сервер отдаёт это поле с самого
    /// начала, но в клиентском DTO его не было — Json.NET молча выбрасывал значение, и лечение
    /// вампиризмом не попадало ни в лог боя, ни в HP-бар.
    /// </summary>
    [JsonProperty("attackerHealed")]       public int     AttackerHealed      { get; set; }
}

/// <summary>Ответ POST /api/combat/engage и GET /api/combat/{id}.</summary>
public sealed class CombatStateResponse
{
    [JsonProperty("sessionId")]       public long                        SessionId       { get; set; }
    [JsonProperty("state")]           public string                      State           { get; set; }

    /// <summary>Номер хода в бою. Сервер отдавал его и раньше — в клиентском DTO поля не было.</summary>
    [JsonProperty("turnNumber")]      public int                         TurnNumber      { get; set; }

    [JsonProperty("finished")]        public bool                        Finished        { get; set; }
    [JsonProperty("winnerSide")]      public string                      WinnerSide      { get; set; }
    [JsonProperty("you")]             public CombatParticipantView       You             { get; set; }
    [JsonProperty("yourOpponent")]    public CombatParticipantView       YourOpponent    { get; set; }

    /// <summary>
    /// Полный состав сторон — для отрисовки замеса N×M. Сервер отдаёт оба списка, клиент их
    /// терял: в DTO были только You и YourOpponent, поэтому нарисовать бой «трое на двое»
    /// было нечем. UI пока не использует, но данные теперь доезжают.
    /// </summary>
    [JsonProperty("sideA")]           public List<CombatParticipantView> SideA           { get; set; }
    [JsonProperty("sideB")]           public List<CombatParticipantView> SideB           { get; set; }

    [JsonProperty("isYourTurn")]      public bool                        IsYourTurn      { get; set; }
    [JsonProperty("turnDeadlineUtc")] public DateTime?                   TurnDeadlineUtc { get; set; }
    [JsonProperty("secondsLeft")]     public int?                        SecondsLeft     { get; set; }
    [JsonProperty("recentActions")]   public List<CombatHitView>         RecentActions   { get; set; }
}

/// <summary>Ответ POST /api/combat/action и POST /api/combat/{id}/skip.</summary>
public sealed class CombatTurnResultResponse
{
    [JsonProperty("sessionId")]       public long                  SessionId       { get; set; }
    [JsonProperty("finished")]        public bool                  Finished        { get; set; }
    [JsonProperty("winnerSide")]      public string                WinnerSide      { get; set; }
    [JsonProperty("yourHit")]         public CombatHitView         YourHit         { get; set; }
    [JsonProperty("responseHits")]    public List<CombatHitView>   ResponseHits    { get; set; }
    [JsonProperty("you")]             public CombatParticipantView You             { get; set; }
    [JsonProperty("yourOpponent")]    public CombatParticipantView YourOpponent    { get; set; }
    [JsonProperty("isYourTurn")]      public bool                  IsYourTurn      { get; set; }
    [JsonProperty("turnDeadlineUtc")] public DateTime?             TurnDeadlineUtc { get; set; }

    /// <summary>
    /// Награда за бой, если ЭТОТ ход его завершил победой. null — награды не было вовсе:
    /// бой не закончен, закончен не победой, или добил союзник (в N×M награда идёт тому, кто
    /// бил моба лично). Отличать null от нулевой награды обязательно — это разные вещи.
    /// </summary>
    [JsonProperty("reward")]          public CombatRewardView      Reward          { get; set; }

    /// <summary>Повышение уровня этим боем. null — уровень не менялся.</summary>
    [JsonProperty("levelUp")]         public CombatLevelUpView     LevelUp         { get; set; }
}

/// <summary>
/// Одна строка дропа в окне награды: что упало и сколько.
/// Снимок на момент боя — название и редкость скопированы, а не подтянуты из живого шаблона.
/// Роллов статов здесь намеренно нет: они видны в инвентаре, окну награды не нужны.
/// </summary>
public sealed class CombatRewardItemView
{
    [JsonProperty("templateId")] public long   TemplateId { get; set; }

    /// <summary>Код шаблона — на будущее под иконки.</summary>
    [JsonProperty("code")]       public string Code       { get; set; }

    [JsonProperty("name")]       public string Name       { get; set; }

    /// <summary>Редкость (common/rare/...) — по ней красится строка дропа.</summary>
    [JsonProperty("rarity")]     public string Rarity     { get; set; }

    [JsonProperty("quantity")]   public int    Quantity   { get; set; }
}

/// <summary>
/// Награда за бой: золото, опыт, выпавшие вещи и следы «Запаса сил».
/// Сервер отдаёт это с 14 июля, но в клиентском DTO полей не было — Json.NET молча выбрасывал
/// весь блок, и попап результата показывал заглушку «Дроп: см. инвентарь» (TD-C33).
/// </summary>
public sealed class CombatRewardView
{
    [JsonProperty("gold")]       public int    Gold       { get; set; }
    [JsonProperty("experience")] public int    Experience { get; set; }

    /// <summary>
    /// Что реально упало. Пустой список — дропа не было (золото и опыт при этом могли начислиться).
    /// Подвешенный групповой лут (режим «вручную») сюда не входит: он ещё никому не принадлежит,
    /// получателя выбирает лидер группы.
    /// </summary>
    [JsonProperty("items")]      public List<CombatRewardItemView> Items { get; set; }

    /// <summary>Применился ли бонус «Запаса сил» к этой награде.</summary>
    [JsonProperty("restedBonusApplied")] public bool   RestedBonusApplied { get; set; }

    /// <summary>Режим применённого бонуса. null — бонус не применялся.</summary>
    [JsonProperty("restedMode")]         public string RestedMode         { get; set; }

    /// <summary>Сколько зарядов «Запаса сил» осталось после боя.</summary>
    [JsonProperty("restedChargesLeft")]  public int    RestedChargesLeft  { get; set; }
}

/// <summary>Повышение уровня за бой. Мультилевелап возможен: NewLevel может быть больше OldLevel на 2+.</summary>
public sealed class CombatLevelUpView
{
    [JsonProperty("oldLevel")] public int OldLevel { get; set; }
    [JsonProperty("newLevel")] public int NewLevel { get; set; }
}

/// <summary>
/// Ответ GET /api/combat/last-reward — снимок награды за последний бой.
/// <para>
/// Зачем: награда приходит в ответе на добивающий ход ровно один раз. Если он не доехал
/// (обрыв, сворачивание приложения, вылет), золото и вещи уже начислены, но показать их было бы
/// нечем. Сервер пишет снимок в транзакции выдачи лута, поэтому перечитать можно всегда.
/// </para>
/// <para>null в ответе — персонаж ещё не получал награду ни за один бой.</para>
/// </summary>
public sealed class LastBattleRewardResponse
{
    [JsonProperty("sessionId")] public long              SessionId  { get; set; }
    [JsonProperty("awardedAt")] public DateTime          AwardedAt  { get; set; }
    [JsonProperty("reward")]    public CombatRewardView  Reward     { get; set; }
    [JsonProperty("levelUp")]   public CombatLevelUpView LevelUp    { get; set; }
}

/// <summary>Одна комбо-последовательность персонажа.</summary>
public sealed class CombatComboDto
{
    [JsonProperty("level")]    public int      Level    { get; set; }
    [JsonProperty("sequence")] public string[] Sequence { get; set; }
    [JsonProperty("finisher")] public string   Finisher { get; set; }
}

/// <summary>Ответ GET /api/character/combos.</summary>
public sealed class CombosResponse
{
    [JsonProperty("combos")] public List<CombatComboDto> Combos { get; set; }
}

/// <summary>Один слот лоадаута расходки.</summary>
public sealed class CombatLoadoutSlotDto
{
    [JsonProperty("slotIndex")]            public short   SlotIndex           { get; set; }
    [JsonProperty("consumableTemplateId")] public long?   ConsumableTemplateId { get; set; }
    [JsonProperty("consumableCode")]       public string  ConsumableCode       { get; set; }
    [JsonProperty("quantityInInventory")]  public int     QuantityInInventory  { get; set; }
}

/// <summary>Ответ GET /api/consumables/loadout.</summary>
public sealed class CombatLoadoutResponse
{
    [JsonProperty("totalSlots")] public int                      TotalSlots { get; set; }
    [JsonProperty("slots")]      public List<CombatLoadoutSlotDto> Slots    { get; set; }
}

/// <summary>Ответ POST /api/combat/consume.</summary>
public sealed class CombatConsumeResponse
{
    [JsonProperty("sessionId")]  public long                  SessionId  { get; set; }
    [JsonProperty("you")]        public CombatParticipantView You        { get; set; }
    [JsonProperty("finished")]   public bool                  Finished   { get; set; }
    [JsonProperty("winnerSide")] public string                WinnerSide { get; set; }
}
