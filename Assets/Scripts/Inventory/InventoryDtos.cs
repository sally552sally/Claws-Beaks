using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// ─── Запросы ──────────────────────────────────────────────────────────────────

/// <summary>Запрос действия с предметом по InstanceId (надеть/снять/починить/выбросить/сундук).</summary>
public sealed class ItemActionRequestDto
{
    [JsonProperty("itemInstanceId")] public long ItemInstanceId { get; set; }
}

// ─── Общие модели предмета ──────────────────────────────────────────────────────

/// <summary>Одна вещь в инвентаре/сундуке (зеркало серверного InventoryItemDto).</summary>
public sealed class InventoryItemDto
{
    [JsonProperty("instanceId")]        public long   InstanceId       { get; set; }
    [JsonProperty("code")]              public string Code             { get; set; }
    [JsonProperty("name")]              public string Name             { get; set; }
    /// <summary>backpack / equipped / chest.</summary>
    [JsonProperty("container")]         public string Container        { get; set; }
    /// <summary>weapon_main / weapon_off / body / legs / hands / head / belt (null если не надето).</summary>
    [JsonProperty("equipSlot")]         public string EquipSlot        { get; set; }
    /// <summary>grey / green / blue / purple / red.</summary>
    [JsonProperty("rarity")]            public string Rarity           { get; set; }
    /// <summary>weapon / body / legs / hands / head / belt / ring / amulet (null у расходки).</summary>
    [JsonProperty("slotCategory")]      public string SlotCategory     { get; set; }
    [JsonProperty("gearStyle")]         public string GearStyle        { get; set; }
    [JsonProperty("setId")]             public long?  SetId            { get; set; }
    [JsonProperty("isTwoHanded")]       public bool   IsTwoHanded      { get; set; }
    [JsonProperty("levelRequirement")]  public int    LevelRequirement { get; set; }
    [JsonProperty("durabilityMax")]     public int    DurabilityMax    { get; set; }
    [JsonProperty("durabilityCurrent")] public int    DurabilityCurrent { get; set; }
    [JsonProperty("isBroken")]          public bool   IsBroken         { get; set; }
    [JsonProperty("rolledStrength")]    public int    RolledStrength   { get; set; }
    [JsonProperty("rolledAgility")]     public int    RolledAgility    { get; set; }
    [JsonProperty("rolledIntuition")]   public int    RolledIntuition  { get; set; }
    [JsonProperty("rolledDefense")]     public int    RolledDefense    { get; set; }
    [JsonProperty("rolledVitality")]    public int    RolledVitality   { get; set; }
    [JsonProperty("rolledDamage")]      public int    RolledDamage     { get; set; }
    [JsonProperty("rolledHp")]          public int    RolledHp         { get; set; }
}

// ─── Ответы ─────────────────────────────────────────────────────────────────

/// <summary>Ответ GET /api/gear/inventory.</summary>
public sealed class InventoryResponseDto
{
    [JsonProperty("backpackCapacity")]   public int                    BackpackCapacity   { get; set; }
    [JsonProperty("backpackUsed")]       public int                    BackpackUsed       { get; set; }
    [JsonProperty("equipped")]           public List<InventoryItemDto> Equipped           { get; set; } = new();
    [JsonProperty("backpack")]           public List<InventoryItemDto> Backpack           { get; set; } = new();
    /// <summary>Доступен ли сундук в текущей локации (UX-флаг; сервер всё равно проверяет).</summary>
    [JsonProperty("chestAvailableHere")] public bool                   ChestAvailableHere { get; set; }
}

/// <summary>Ответ GET /api/gear/chest.</summary>
public sealed class ChestResponseDto
{
    [JsonProperty("available")]        public bool                   Available        { get; set; }
    [JsonProperty("items")]            public List<InventoryItemDto> Items            { get; set; } = new();
    [JsonProperty("backpackCapacity")] public int                    BackpackCapacity { get; set; }
    [JsonProperty("backpackUsed")]     public int                    BackpackUsed     { get; set; }
}

/// <summary>Ответ перекладки рюкзак↔сундук.</summary>
public sealed class ChestMoveResponseDto
{
    [JsonProperty("instanceId")] public long   InstanceId { get; set; }
    [JsonProperty("container")]  public string Container  { get; set; }
}

/// <summary>Ответ POST /api/gear/repair.</summary>
public sealed class RepairResponseDto
{
    [JsonProperty("instanceId")]        public long InstanceId        { get; set; }
    [JsonProperty("durabilityMax")]     public int  DurabilityMax     { get; set; }
    [JsonProperty("durabilityCurrent")] public int  DurabilityCurrent { get; set; }
    [JsonProperty("goldSpent")]         public int  GoldSpent         { get; set; }
    [JsonProperty("goldLeft")]          public long GoldLeft          { get; set; }
}

// ─── Вкладка «Эффекты» (расходка) ──────────────────────────────────────────────

/// <summary>Один стак расходки в инвентаре (вкладка «Эффекты»).</summary>
public sealed class ConsumableStackDto
{
    [JsonProperty("templateId")]         public long      TemplateId        { get; set; }
    [JsonProperty("code")]               public string    Code              { get; set; }
    [JsonProperty("name")]               public string    Name              { get; set; }
    [JsonProperty("kind")]               public string    Kind              { get; set; }
    [JsonProperty("quantity")]           public int       Quantity          { get; set; }
    /// <summary>Момент истечения (UTC). null — бессрочный.</summary>
    [JsonProperty("ttlExpiresUtc")]      public DateTime? TtlExpiresUtc     { get; set; }
    /// <summary>Секунд до истечения по серверному времени. null — бессрочный.</summary>
    [JsonProperty("secondsUntilExpire")] public long?     SecondsUntilExpire { get; set; }
}

/// <summary>Ответ GET /api/consumables/stacks.</summary>
public sealed class ConsumableStacksResponseDto
{
    [JsonProperty("stacks")] public List<ConsumableStackDto> Stacks { get; set; } = new();
}
