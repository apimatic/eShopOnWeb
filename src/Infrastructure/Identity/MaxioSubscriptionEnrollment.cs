using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Local correlation record; Maxio remains the billing system of record.</summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public string UniquenessToken { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
