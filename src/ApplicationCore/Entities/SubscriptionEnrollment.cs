using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local correlation data for an eShopOnWeb enrollment in Maxio Advanced Billing.
/// Maxio remains the billing system of record; this row only makes retries safe and
/// preserves the Maxio IDs used to correlate the two systems.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public int MaxioCustomerId { get; set; }

    public int? MaxioSubscriptionId { get; set; }

    public string UniquenessToken { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
