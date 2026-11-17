using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// An application-owned claim that makes a payment operation safe to invoke twice.
/// The key is the primary key, so a second concurrent claim is refused by the store.
/// </summary>
public class PaymentOperation
{
    public const string Claimed = "CLAIMED";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";

    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? BuyerId { get; set; }
    public int? OrderId { get; set; }
    public string? ResultId { get; set; }
    public string State { get; set; } = Claimed;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
