using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public long AmountCents { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingPayment;

    public string? CustomerIdentityId { get; set; }
    public string? PaymentInstrumentId { get; set; }

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public int PaymentAttempts { get; set; }

    public string? TransferId { get; set; }
    public string? TransferState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetProceedsCents { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    public long RefundedAmountCents { get; set; }

    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<RefundRecord> Refunds { get; set; } = new();
}
