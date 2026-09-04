using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The local idempotency ledger for a Maxio subscription signup.
/// Maxio remains the system of record for the customer and subscription state.
/// </summary>
public class SubscriptionMapping
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public required string ProductHandle { get; set; }

    public required string SubscriptionReference { get; set; }

    public int MaxioCustomerId { get; set; }

    public int? MaxioSubscriptionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
