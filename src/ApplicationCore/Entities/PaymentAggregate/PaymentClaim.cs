using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public static class PaymentClaimStatus
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Released = "Released";
    public const string Unknown = "Unknown";
}

public class PaymentClaim : IAggregateRoot
{
    [MaxLength(128)]
    public string Scope { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = PaymentClaimStatus.Pending;

    [MaxLength(128)]
    public string? Reference { get; set; }

    [MaxLength(4000)]
    public string? ResultJson { get; set; }

    [MaxLength(512)]
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }
}
