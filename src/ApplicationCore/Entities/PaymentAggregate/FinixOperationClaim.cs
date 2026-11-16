using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A claim taken before a non-idempotent Finix write. The primary key makes a
/// second concurrent request fail at the store rather than at the provider.
/// </summary>
public class FinixOperationClaim : IAggregateRoot
{
    public string ClaimKey { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
