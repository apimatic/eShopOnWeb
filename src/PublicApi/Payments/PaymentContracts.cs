using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)] public List<CreateOrderItemRequest> Items { get; init; } = new();
    [Required] public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; init; }
    [Range(1, 1000)] public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; init; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; init; } = string.Empty;
    [MaxLength(60)] public string State { get; init; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; init; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; init; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardInput? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardInput Card { get; init; } = new();
}

public sealed class CardInput
{
    [Required, MaxLength(300)] public string Name { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9 ]{13,23}$")] public string Number { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; init; } = string.Empty;
    [Required] public BillingAddressInput BillingAddress { get; init; } = new();
}

public sealed class BillingAddressInput
{
    [Required, StringLength(2, MinimumLength = 2)] public string CountryCode { get; init; } = string.Empty;
    [MaxLength(300)] public string? AddressLine1 { get; init; }
    [MaxLength(300)] public string? AddressLine2 { get; init; }
    [MaxLength(120)] public string? City { get; init; }
    [MaxLength(300)] public string? State { get; init; }
    [MaxLength(60)] public string? PostalCode { get; init; }
}

public sealed class CreateRefundRequest
{
    [Range(typeof(decimal), "0.01", "9999999999999")] public decimal? Amount { get; init; }
    [Required, MaxLength(200)] public string IdempotencyKey { get; init; } = string.Empty;
    [MaxLength(255)] public string? Note { get; init; }
}

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string PaymentStatus,
    string FulfilmentStatus,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? CardBrand,
    string? CardLastDigits,
    IReadOnlyCollection<OrderItemResponse> Items,
    IReadOnlyCollection<RefundResponse> Refunds);

public sealed record PaymentMethodResponse(
    int PaymentMethodId, string Brand, string LastDigits, string? Expiry, DateTimeOffset CreatedAt);

public sealed record ReconciliationTransaction(
    string TransactionId, string? PayPalReferenceId, string? EventCode, string? Status,
    DateTimeOffset? InitiatedAt, decimal? Amount, string? Currency, decimal? Fee,
    string? InvoiceId, string? CustomField, int? OrderId);

public sealed record ReconciliationMissingLocal(
    string PayPalTransactionId, string? PayPalReferenceId, decimal? Amount, string? Currency);

public sealed record ReconciliationMissingPayPal(
    int OrderId, string Kind, string PayPalId, decimal Amount, string Currency, DateTimeOffset OccurredAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From, DateTimeOffset To,
    IReadOnlyCollection<ReconciliationTransaction> Transactions,
    IReadOnlyCollection<ReconciliationMissingLocal> PayPalOnly,
    IReadOnlyCollection<ReconciliationMissingPayPal> EShopOnly);

public sealed record PayPalOrderResult(string Id, string Status);
public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string? CardBrand, string? CardLastDigits,
    bool RequiresPayerAction, string? CaptureId);
public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, string Currency,
    decimal Fee, decimal Net, DateTimeOffset CreatedAt);
public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, string Currency,
    DateTimeOffset CreatedAt);
public sealed record PayPalVaultResult(string Id, string? CustomerId, string Status,
    string Brand, string LastDigits, string? Expiry, bool RequiresPayerAction);
public sealed record PayPalTransactionResult(string TransactionId, string? PayPalReferenceId,
    string? EventCode, string? Status, DateTimeOffset? InitiatedAt, decimal? Amount,
    string? Currency, decimal? Fee, string? InvoiceId, string? CustomField);
public sealed record PayPalTransactionPage(IReadOnlyCollection<PayPalTransactionResult> Transactions,
    int Page, int TotalPages);
