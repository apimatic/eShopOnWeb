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
    [Required, MaxLength(18)] public string PostalCode { get; init; } = string.Empty;
}

public sealed class PayOrderRequest : IValidatableObject
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((Card is null) == (PaymentMethodId is null))
            yield return new ValidationResult("Supply either card or paymentMethodId, but not both.",
                new[] { nameof(Card), nameof(PaymentMethodId) });
        if (PaymentMethodId <= 0)
            yield return new ValidationResult("paymentMethodId must be positive.", new[] { nameof(PaymentMethodId) });
    }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardRequest Card { get; init; } = new();
}

public sealed class CardRequest
{
    [Required, MaxLength(300)] public string Name { get; init; } = string.Empty;
    [Required, CreditCard] public string Number { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; init; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; init; } = string.Empty;
    [Required] public BillingAddressRequest BillingAddress { get; init; } = new();
}

public sealed class BillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; init; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; init; }
    [Required, MaxLength(120)] public string City { get; init; } = string.Empty;
    [MaxLength(120)] public string State { get; init; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; init; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; init; } = string.Empty;
}

public sealed class CreateRefundRequest
{
    [Range(typeof(decimal), "0.01", "9999999.99")] public decimal? Amount { get; init; }
    [Required, MinLength(1), MaxLength(128)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, decimal Total, string Currency, string PaymentStatus);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry);
public sealed record RefundResponse(int RefundId, string PayPalRefundId, string Status, decimal Amount, string Currency);

public sealed record OrderPaymentResponse(
    int OrderId,
    string PaymentStatus,
    string FulfillmentStatus,
    decimal Total,
    string? Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetProceeds,
    decimal RefundedAmount,
    IReadOnlyList<OrderRefundResponse> Refunds);

public sealed record OrderRefundResponse(int RefundId, string? PayPalRefundId, string Status,
    decimal Amount, string Currency, DateTimeOffset CreatedAt);

public sealed record MyOrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string? Currency,
    string PaymentStatus,
    string FulfillmentStatus,
    IReadOnlyList<MyOrderItemResponse> Items,
    OrderPaymentResponse Payment);

public sealed record MyOrderItemResponse(int CatalogItemId, string Name, int Quantity, decimal UnitPrice);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntryResponse> Entries,
    IReadOnlyList<LocalOnlyPaymentResponse> LocalOnly);

public sealed record ReconciliationEntryResponse(
    string PayPalTransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    int? OrderId,
    string MatchStatus);

public sealed record LocalOnlyPaymentResponse(int OrderId, string ResourceType, string PayPalId,
    DateTimeOffset OccurredAt, decimal Amount, string Currency);
