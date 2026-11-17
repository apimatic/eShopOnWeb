using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    public List<OrderItemInput> Items { get; set; } = new();

    public ShipToAddressInput? ShipToAddress { get; set; }
}

public sealed class OrderItemInput
{
    public int CatalogItemId { get; set; }

    public int Quantity { get; set; }
}

public sealed class ShipToAddressInput
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public int OrderId { get; set; }

    public int? PaymentMethodId { get; set; }

    public CardDetails? Card { get; set; }
}

public sealed class RefundOrderRequest
{
    public int OrderId { get; set; }

    public long? AmountInCents { get; set; }

    public decimal? Amount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    public string? RequestId { get; set; }

    public string? Alias { get; set; }

    public CardDetails Card { get; set; } = new();
}

public sealed class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }

    public DateTimeOffset To { get; set; }
}

public sealed class OrderLineView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class RefundView
{
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateOrderResult
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public List<OrderLineView> Items { get; set; } = new();
}

public sealed class PaymentView
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long? AuthorizedAmountCents { get; set; }
    public string? VoidState { get; set; }
    public string? CapturedTransferId { get; set; }
    public string? CapturedTransferState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public bool? FeesComplete { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<OrderLineView> Items { get; set; } = new();
    public List<RefundView> Refunds { get; set; } = new();
}

public sealed class RefundResult
{
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SavedPaymentMethodView
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Alias { get; set; }
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class FinixTransactionView
{
    public string Id { get; set; } = string.Empty;
    public long? AmountCents { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? ParentTransferId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
}

public sealed class FinixAuthorizationView
{
    public string Id { get; set; } = string.Empty;
    public long? AmountCents { get; set; }
    public string? State { get; set; }
    public bool? IsVoid { get; set; }
    public string? VoidState { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
}

public sealed class UnmatchedAppPaymentView
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CapturedTransferId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UnmatchedAppRefundView
{
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReconciliationResult
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public bool TransfersComplete { get; set; }
    public bool AuthorizationsComplete { get; set; }
    public int TransferPagesWalked { get; set; }
    public int AuthorizationPagesWalked { get; set; }
    public int FinixTransferCount { get; set; }
    public int FinixAuthorizationCount { get; set; }
    public int UnmatchedFinixTransferCount { get; set; }
    public int UnmatchedFinixAuthorizationCount { get; set; }
    public int UnmatchedAppPaymentCount { get; set; }
    public int UnmatchedAppRefundCount { get; set; }
    public List<FinixTransactionView> FinixTransfers { get; set; } = new();
    public List<FinixAuthorizationView> FinixAuthorizations { get; set; } = new();
    public List<FinixTransactionView> UnmatchedFinixTransfers { get; set; } = new();
    public List<FinixAuthorizationView> UnmatchedFinixAuthorizations { get; set; } = new();
    public List<UnmatchedAppPaymentView> UnmatchedAppPayments { get; set; } = new();
    public List<UnmatchedAppRefundView> UnmatchedAppRefunds { get; set; } = new();
}
