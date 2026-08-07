using System.Collections.Generic;
using Newtonsoft.Json;

// ─── GET /api/shop/repair-quote ──────────────────────────────────────────────

/// <summary>Одна вещь в предварительном расчёте ремонта.</summary>
public sealed class RepairQuoteItemDto
{
    [JsonProperty("instanceId")]         public long   InstanceId        { get; set; }
    [JsonProperty("name")]               public string Name              { get; set; }
    [JsonProperty("durabilityCurrent")]  public int    DurabilityCurrent { get; set; }
    [JsonProperty("durabilityMax")]      public int    DurabilityMax     { get; set; }

    /// <summary>Каким станет максимум после ремонта (правило «−1 за ремонт»).</summary>
    [JsonProperty("durabilityMaxAfter")] public int    DurabilityMaxAfter { get; set; }

    /// <summary>Цена ремонта этой вещи. Считает сервер — формулы на клиенте нет.</summary>
    [JsonProperty("cost")]               public int    Cost              { get; set; }
}

/// <summary>
/// Что и почём починится. Клиент НИЧЕГО из этого не вычисляет: и список вещей, и цены приходят
/// с сервера, потому что там же живёт правило «что подлежит ремонту» и балансный коэффициент.
/// </summary>
public sealed class RepairQuoteResponseDto
{
    [JsonProperty("items")]          public List<RepairQuoteItemDto> Items { get; set; }
    [JsonProperty("totalCost")]      public int  TotalCost      { get; set; }
    [JsonProperty("goldAvailable")]  public long GoldAvailable  { get; set; }
    [JsonProperty("canAffordAll")]   public bool CanAffordAll   { get; set; }
    [JsonProperty("skippedWornOut")] public int  SkippedWornOut { get; set; }
}

// ─── POST /api/shop/repair-all ───────────────────────────────────────────────

/// <summary>Одна починенная вещь.</summary>
public sealed class RepairedItemDto
{
    [JsonProperty("instanceId")]        public long   InstanceId        { get; set; }
    [JsonProperty("name")]              public string Name              { get; set; }
    [JsonProperty("durabilityMax")]     public int    DurabilityMax     { get; set; }
    [JsonProperty("durabilityCurrent")] public int    DurabilityCurrent { get; set; }
    [JsonProperty("goldSpent")]         public int    GoldSpent         { get; set; }
}

/// <summary>Результат «починить всё надетое».</summary>
public sealed class RepairAllResponseDto
{
    [JsonProperty("items")]          public List<RepairedItemDto> Items { get; set; }
    [JsonProperty("goldSpent")]      public int  GoldSpent      { get; set; }
    [JsonProperty("goldLeft")]       public long GoldLeft       { get; set; }
    [JsonProperty("skippedWornOut")] public int  SkippedWornOut { get; set; }
}
