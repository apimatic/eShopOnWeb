using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : IAggregateRoot
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    [Required]
    [MaxLength(128)]
    public string FinixRefundId { get; set; } = string.Empty;

    public long AmountCents { get; set; }

    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public RefundStatus Status { get; set; }

    [MaxLength(64)]
    public string? FailureCode { get; set; }

    [MaxLength(512)]
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
