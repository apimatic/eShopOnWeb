using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; init; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; init; }

    [Range(1, 1000)]
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; init; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; init; } = string.Empty;
    [MaxLength(60)] public string State { get; init; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; init; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; init; } = string.Empty;
}

public sealed class PayOrderRequest : IValidatableObject
{
    public PaymentCardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((Card == null) == (PaymentMethodId == null))
        {
            yield return new ValidationResult(
                "Supply exactly one of card or paymentMethodId.",
                new[] { nameof(Card), nameof(PaymentMethodId) });
        }
        if (PaymentMethodId <= 0)
        {
            yield return new ValidationResult("paymentMethodId must be positive.",
                new[] { nameof(PaymentMethodId) });
        }
    }
}

public sealed class SavePaymentMethodRequest
{
    [Required]
    public PaymentCardRequest Card { get; init; } = new();
}

public sealed class PaymentCardRequest
{
    [Required, RegularExpression("^[0-9 ]{13,23}$")]
    public string Number { get; init; } = string.Empty;

    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    public string Expiry { get; init; } = string.Empty;

    [Required, RegularExpression("^[0-9]{3,4}$")]
    public string SecurityCode { get; init; } = string.Empty;

    [Required, MaxLength(300)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public CardBillingAddressRequest BillingAddress { get; init; } = new();

    public PaymentCard ToGatewayModel() => new(Number, Expiry, SecurityCode, Name,
        new CardBillingAddress(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
            BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode,
            BillingAddress.CountryCode));
}

public sealed class CardBillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; init; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; init; }
    [Required, MaxLength(120)] public string City { get; init; } = string.Empty;
    [MaxLength(300)] public string State { get; init; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; init; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; init; } = string.Empty;
}

public sealed class RefundOrderRequest
{
    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal? Amount { get; init; }

    [Required, MaxLength(108)]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, OrderResponse Order);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string PaymentState,
    decimal Total,
    string Currency,
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
    decimal? RefundableAmount,
    IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<RefundResponse> Refunds);

public sealed record OrderItemResponse(
    int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record RefundResponse(
    string RefundId, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt);

public sealed record CreateRefundResponse(string RefundId, OrderResponse Order);

public sealed record PaymentMethodResponse(
    int PaymentMethodId, string Brand, string LastDigits, string Expiry,
    string? CardholderName, DateTimeOffset CreatedAt);

public sealed record CreatePaymentMethodResponse(
    int PaymentMethodId, PaymentMethodResponse PaymentMethod);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset PayPalDataThrough,
    IReadOnlyList<ReconciliationPayPalRecord> PayPalRecords,
    IReadOnlyList<ReconciliationEShopRecord> EShopRecords);

public sealed record ReconciliationPayPalRecord(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    int? OrderId,
    bool Matched);

public sealed record ReconciliationEShopRecord(
    int OrderId,
    string Kind,
    string PayPalId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    bool Matched);

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string code, string message,
        string? operatorAction = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        OperatorAction = operatorAction;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? OperatorAction { get; }
}
