using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardAddressRequest
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "USA";
}

public class CardDetailsRequest
{
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }
    public CardDetailsRequest? Card { get; set; }

    public CardDetails? ResolveCard()
    {
        if (Card is not null)
        {
            return Card.ResolveCard();
        }

        if (string.IsNullOrWhiteSpace(Number))
        {
            return null;
        }

        return new CardDetails(
            Number!,
            ExpirationMonth ?? 0,
            ExpirationYear ?? 0,
            SecurityCode,
            Name ?? string.Empty,
            Address is null
                ? null
                : new CardAddress(Address.Line1, Address.Line2, Address.City, Address.Region,
                    Address.PostalCode, Address.Country));
    }
}

public class PayOrderRequest : CardDetailsRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class SavePaymentMethodRequest : CardDetailsRequest
{
}

public class FulfilOrderRequest
{
    [JsonIgnore] public int OrderId { get; set; }
}

public class CancelOrderRequest
{
    [JsonIgnore] public int OrderId { get; set; }
}

public class RefundOrderRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public long? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyId { get; set; }

    public long? ResolveAmountCents()
    {
        if (AmountCents.HasValue)
        {
            return AmountCents.Value;
        }

        if (Amount.HasValue)
        {
            return checked((long)decimal.Round(Amount.Value * 100m, 0, MidpointRounding.AwayFromZero));
        }

        return null;
    }

    public string ResolveIdempotencyKey() => IdempotencyKey ?? IdempotencyId ?? string.Empty;
}

public class DeletePaymentMethodRequest
{
    [JsonIgnore] public int PaymentMethodId { get; set; }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentResponse
{
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureStatus { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? FinixFeeCents { get; set; }
    public decimal? FinixFee { get; set; }
    public decimal? NetProceedsCents { get; set; }
    public decimal? NetProceeds { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string FinixTransferId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavedPaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFour { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public PaymentResponse Payment { get; set; } = new();
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public RefundResponse Refund { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public SavedPaymentMethodResponse PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse
{
    public List<SavedPaymentMethodResponse> PaymentMethods { get; set; } = new();
}
