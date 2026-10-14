using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>Durable local idempotency/reconciliation record; Maxio remains the billing system of record.</summary>
public class MaxioOperationClaim : IAggregateRoot
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? MaxioResourceId { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
