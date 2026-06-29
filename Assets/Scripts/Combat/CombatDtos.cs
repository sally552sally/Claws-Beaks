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
}

/// <summary>Ответ POST /api/combat/engage и GET /api/combat/{id}.</summary>
public sealed class CombatStateResponse
{
    [JsonProperty("sessionId")]       public long                        SessionId       { get; set; }
    [JsonProperty("state")]           public string                      State           { get; set; }
    [JsonProperty("finished")]        public bool                        Finished        { get; set; }
    [JsonProperty("winnerSide")]      public string                      WinnerSide      { get; set; }
    [JsonProperty("you")]             public CombatParticipantView       You             { get; set; }
    [JsonProperty("yourOpponent")]    public CombatParticipantView       YourOpponent    { get; set; }
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
