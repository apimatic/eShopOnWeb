using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderLineRequest> Items { get; init; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class CreateOrderLineRequest
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

public sealed class CardRequest
{
    [Required, MaxLength(300)] public string Name { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9 ]{13,23}$")] public string Number { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; init; } = string.Empty;
    [Required] public BillingAddressRequest BillingAddress { get; init; } = new();
}

public sealed class BillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; init; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; init; }
    [Required, MaxLength(120)] public string City { get; init; } = string.Empty;
    [MaxLength(300)] public string? State { get; init; }
    [Required, MaxLength(60)] public string PostalCode { get; init; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; init; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed class SavePaymentMethodRequest
{
    [Required, MaxLength(100)] public string Alias { get; init; } = string.Empty;
    [Required] public CardRequest Card { get; init; } = new();
}

public sealed class RefundOrderRequest
{
    [Required, MinLength(1), MaxLength(108)] public string IdempotencyKey { get; init; } = string.Empty;
    [Range(typeof(decimal), "0.01", "9999999999999.99")] public decimal? Amount { get; init; }
    [MaxLength(255)] public string? Note { get; init; }
}

public sealed record CreateOrderResponse(int OrderId, decimal Total, string Currency, string PaymentState);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Alias, string Brand, string Last4, string Expiry);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, string Currency);

public sealed record RefundDto(string RefundId, string? PayPalRefundId, string Status, decimal Amount,
    string Currency, decimal? PayPalFeeRefunded, decimal? NetAmountDebited);

public sealed record PaymentDto(
    string State,
    string Currency,
    string? PayPalOrderId,
    string? PayPalOrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? AuthorizedAmount,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal RefundedAmount,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyCollection<RefundDto> Refunds);

public sealed record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderDto(int OrderId, DateTimeOffset OrderDate, decimal Total, string FulfilmentStatus,
    IReadOnlyCollection<OrderItemDto> Items, PaymentDto? Payment);

public sealed record PayOrderResponse(int OrderId, PaymentDto Payment);
public sealed record FulfilOrderResponse(int OrderId, string FulfilmentStatus, PaymentDto Payment);
public sealed record CancelOrderResponse(int OrderId, string FulfilmentStatus, PaymentDto Payment);

public sealed record ReconciliationTransactionDto(
    string PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomId,
    int? OrderId,
    string MatchStatus);

public sealed record ReconciliationMissingLocalRecordDto(int OrderId, string RecordType,
    string PayPalId, DateTimeOffset OccurredAt, decimal Amount, string Currency, string Status);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyCollection<ReconciliationTransactionDto> Transactions,
    IReadOnlyCollection<ReconciliationMissingLocalRecordDto> LocalRecordsMissingFromPayPal);
