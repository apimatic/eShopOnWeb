using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Price per interval in the site's currency.</summary>
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    /// <summary>The plan's brand (its Maxio product family).</summary>
    public string? Brand { get; set; }
}

public class PlanSummaryDto
{
    public int? Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    /// <summary>True when the shopper owes nothing on the subscription.</summary>
    public bool Paid { get; set; }
    /// <summary>Amount currently owed (never negative).</summary>
    public decimal AmountDue { get; set; }
    public PlanSummaryDto Plan { get; set; } = new();
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public class UsageRecordDto
{
    public long? Id { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
}

public class ComponentUsageDto
{
    public int ComponentId { get; set; }
    public string? Name { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public List<UsageRecordDto> Records { get; set; } = new();
}

public class SubscriptionUsageDto
{
    public int SubscriptionId { get; set; }
    public string Date { get; set; } = string.Empty;
    public List<ComponentUsageDto> Components { get; set; } = new();
}
