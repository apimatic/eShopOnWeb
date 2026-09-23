using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------- Requests ----------

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in ISO-8601 YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(Number, Expiry, SecurityCode, CardholderName,
        BillingAddress?.ToCardBillingAddress());
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public CardBillingAddress ToCardBillingAddress() =>
        new(AddressLine1, AddressLine2, AdminArea1, AdminArea2, PostalCode, CountryCode);
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedCardId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedCardId { get; set; }
}

public class RefundRequestDto
{
    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. May instead be sent as the <c>Idempotency-Key</c> header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class SavePaymentMethodRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public CardDetails ToCardDetails() => new(Number, Expiry, SecurityCode, CardholderName,
        BillingAddress?.ToCardBillingAddress());
}

// ---------- Responses ----------

public class CreateOrderResponse
{
    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = nameof(OrderPaymentStatus.AwaitingPayment);
}

public class RefundDto
{
    public string? RefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal Amount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public OrderPaymentResponse? Payment { get; set; }
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class RefundResponse
{
    /// <summary>Top-level identifier of the created refund.</summary>
    public string? RefundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class SavedCardResponse
{
    /// <summary>Top-level identifier of the saved card (payment method).</summary>
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class SavedCardsResponse
{
    public List<SavedCardResponse> PaymentMethods { get; set; } = new();
}

public class ReconciliationResponse
{
    public System.DateTimeOffset From { get; set; }
    public System.DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInPayPalCount { get; set; }
    public int OnlyInEShopCount { get; set; }
    public int PayPalTransactionsScanned { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

// ---------- Mapping ----------

public static class PaymentMappers
{
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static OrderPaymentResponse ToResponse(this OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        Currency = payment.Currency,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PaypalFee = payment.PaypalFee,
        NetAmount = payment.NetAmount,
        Refunds = new List<RefundDto>(System.Linq.Enumerable.Select(payment.Refunds, r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status
        }))
    };

    public static SavedCardResponse ToResponse(this SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        LastDigits = card.LastDigits,
        Expiry = card.Expiry,
        CardholderName = card.CardholderName
    };

    public static MyOrderDto ToMyOrderDto(this OrderWithPayment pair) => new()
    {
        OrderId = pair.Order.Id,
        OrderDate = pair.Order.OrderDate,
        Total = pair.Order.Total(),
        PaymentStatus = pair.Payment?.Status.ToString() ?? nameof(OrderPaymentStatus.AwaitingPayment),
        Payment = pair.Payment?.ToResponse()
    };
}
