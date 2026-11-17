using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : IAggregateRoot
{
    [Key]
    public int OrderId { get; set; }

    [Required]
    [MaxLength(256)]
    public string BuyerId { get; set; } = string.Empty;

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";

    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingPayment;

    public FulfilmentStatus FulfilmentStatus { get; set; } = FulfilmentStatus.NotFulfilled;

    [MaxLength(128)]
    public string? AuthorizationId { get; set; }

    [MaxLength(32)]
    public string? AuthorizationState { get; set; }

    public long? AuthorizedAmountCents { get; set; }

    [MaxLength(32)]
    public string? VoidState { get; set; }

    [MaxLength(128)]
    public string? CapturedTransferId { get; set; }

    [MaxLength(32)]
    public string? CapturedTransferState { get; set; }

    public long? CapturedAmountCents { get; set; }

    public long? FeeCents { get; set; }

    public long? NetCents { get; set; }

    public long RefundedAmountCents { get; set; }

    [MaxLength(128)]
    public string? PaymentInstrumentId { get; set; }

    [MaxLength(64)]
    public string? FailureCode { get; set; }

    [MaxLength(512)]
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
