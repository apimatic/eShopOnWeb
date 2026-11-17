using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public List<CreateOrderItemRequest>? OrderItems { get; set; }
    public List<CreateOrderItemRequest>? BasketItems { get; set; }

    public AddressRequest? ShippingAddress { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
    public AddressRequest? Address { get; set; }

    [JsonIgnore]
    public List<CreateOrderItemRequest> EffectiveItems => Items ?? OrderItems ?? BasketItems ?? [];

    [JsonIgnore]
    public AddressRequest? EffectiveAddress => ShippingAddress ?? ShipToAddress ?? Address;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int? Id { get; set; }
    public int Quantity { get; set; } = 1;
    public int? Units { get; set; }

    [JsonIgnore]
    public int EffectiveCatalogItemId => CatalogItemId != 0 ? CatalogItemId : Id ?? 0;

    [JsonIgnore]
    public int EffectiveQuantity => Units is > 0 ? Units.Value : Quantity;
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? Zip { get; set; }
    public string? PostalCode { get; set; }

    [JsonIgnore]
    public string EffectiveStreet => First(Street, Line1, "1 Main Street");
    [JsonIgnore]
    public string EffectiveCity => First(City, "San Francisco");
    [JsonIgnore]
    public string EffectiveState => First(State, Region, "CA");
    [JsonIgnore]
    public string EffectiveCountry => First(Country, "USA");
    [JsonIgnore]
    public string EffectiveZip => First(ZipCode, Zip, PostalCode, "94105");

    private static string First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Expiration { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? CardholderName { get; set; }
    public string? Name { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    [JsonIgnore]
    public bool HasCardData =>
        !string.IsNullOrWhiteSpace(Number)
        || !string.IsNullOrWhiteSpace(CardNumber)
        || ExpirationMonth.HasValue
        || !string.IsNullOrWhiteSpace(Expiration);

    public CardDetails ToCardDetails()
    {
        var number = FirstNonEmpty(Number, CardNumber)
            ?? throw new PaymentOperationException("Card number is required.", PaymentFailureKind.Validation);

        var (month, year) = ResolveExpiration();
        var address = Address ?? BillingAddress;

        return new CardDetails(
            number,
            month,
            year,
            FirstNonEmpty(SecurityCode, Cvv),
            FirstNonEmpty(CardholderName, Name),
            address is null
                ? null
                : new AddressDetails(
                    address.EffectiveStreet,
                    address.EffectiveCity,
                    address.EffectiveState,
                    address.EffectiveZip,
                    address.EffectiveCountry));
    }

    private (int Month, int Year) ResolveExpiration()
    {
        if (ExpirationMonth.HasValue && ExpirationYear.HasValue)
        {
            return (ExpirationMonth.Value, ExpirationYear.Value);
        }

        if (!string.IsNullOrWhiteSpace(Expiration))
        {
            var parts = Expiration.Split('/', '-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var parsedMonth)
                && int.TryParse(parts[1], out var parsedYear))
            {
                return (parsedMonth, parsedYear);
            }
        }

        throw new PaymentOperationException(
            "ExpirationMonth and ExpirationYear (or Expiration as MM/YYYY) are required.",
            PaymentFailureKind.Validation);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? FirstNonWhiteSpace(params string?[] values) => FirstNonEmpty(values);
}

public class PayOrderRequest : CardRequest
{
    public string? PaymentMethodId { get; set; }
    public string? SavedCardId { get; set; }
    public string? SavedPaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }

    [JsonIgnore]
    public string? EffectivePaymentMethodId => FirstNonEmpty(PaymentMethodId, SavedCardId, SavedPaymentMethodId);

    [JsonIgnore]
    public CardRequest? EffectiveCard => Card ?? (HasCardData ? this : null);

    [JsonIgnore]
    public int OrderId { get; set; }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

public class SavePaymentMethodRequest : CardRequest
{
}

public class RefundOrderRequest
{
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKeySnake { get; set; }

    [JsonPropertyName("idempotency-key")]
    public string? IdempotencyKeyDash { get; set; }

    [JsonIgnore]
    public long? EffectiveAmountCents
    {
        get
        {
            if (AmountCents.HasValue)
            {
                return AmountCents.Value;
            }

            if (Amount.HasValue)
            {
                return decimal.Truncate(Amount.Value) == Amount.Value
                    ? (long)Amount.Value
                    : (long)Math.Round(Amount.Value * 100m, MidpointRounding.AwayFromZero);
            }

            return null;
        }
    }

    [JsonIgnore]
    public string? EffectiveIdempotencyKey =>
        new[] { IdempotencyKey, IdempotencyKeySnake, IdempotencyKeyDash }
            .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k))?.Trim();
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? PictureUri { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class AddressResponse
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount => AmountCents / 100m;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentResponse
{
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long AmountCents { get; set; }

    public string? FinixAuthorizationId { get; set; }
    public string? AuthorizationIdempotencyId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }

    public string? FinixTransferId { get; set; }
    public string? TransferState { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal CapturedAmount => CapturedAmountCents / 100m;
    public decimal FinixFeeCents { get; set; }
    public decimal FinixFee => FinixFeeCents / 100m;
    public decimal NetProceedsCents { get; set; }
    public decimal NetProceeds => NetProceedsCents / 100m;

    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? SavedPaymentMethodId { get; set; }
    public List<RefundResponse> Refunds { get; set; } = [];
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public AddressResponse? ShippingAddress { get; set; }
    public List<OrderItemResponse> Items { get; set; } = [];
    public PaymentResponse Payment { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = [];
    public List<OrderResponse> Items => Orders;
}

public class SavedPaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? FinixPaymentInstrumentId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodsResponse
{
    public List<SavedPaymentMethodResponse> PaymentMethods { get; set; } = [];
    public List<SavedPaymentMethodResponse> Items => PaymentMethods;
}

public class CreatePaymentMethodResponse
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public SavedPaymentMethodResponse PaymentMethod { get; set; } = new();
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class RefundOrderResponse
{
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? State { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount => AmountCents / 100m;
    public long TotalRefundedCents { get; set; }
    public long RemainingRefundableCents { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public class ReconciliationRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<FinixTransactionRecord> FinixTransactions { get; set; } = [];
    public List<EshopTransactionRecord> EshopTransactions { get; set; } = [];
    public List<FinixTransactionRecord> UnmatchedFinixTransactions { get; set; } = [];
    public List<EshopTransactionRecord> UnmatchedEshopTransactions { get; set; } = [];
    public List<FinixTransactionRecord> Transactions => FinixTransactions;
    public List<EshopTransactionRecord> Orders => EshopTransactions;
    public List<FinixTransactionRecord> FinixOnly => UnmatchedFinixTransactions;
    public List<EshopTransactionRecord> EshopOnly => UnmatchedEshopTransactions;
}
