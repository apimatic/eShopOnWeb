namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// How often a plan bills: every <see cref="Length"/> <see cref="Unit"/>s (e.g. 1 "month").
/// </summary>
public class BillingIntervalDto
{
    public int? Length { get; set; }

    /// <summary>Maxio's wire value for the interval unit ("day" or "month"; other values pass through).</summary>
    public string? Unit { get; set; }

    public static BillingIntervalDto? From(int? length, string? unit) =>
        length is null && string.IsNullOrWhiteSpace(unit) ? null : new BillingIntervalDto { Length = length, Unit = unit };
}
