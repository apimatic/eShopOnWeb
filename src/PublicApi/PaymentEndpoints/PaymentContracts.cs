using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
}

public sealed class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; set; } = new();

    public PaymentCardData ToData() => new(Name, Number, Expiry, SecurityCode,
        new PaymentBillingAddress(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
            BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode,
            BillingAddress.CountryCode));
}

public sealed class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed class RefundRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, decimal? PayPalFee,
    decimal? NetAmount, DateTimeOffset CreatedAt);
public sealed record PaymentResponse(string Status, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, decimal? AuthorizedAmount, DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId, string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee,
    decimal? NetAmount, decimal RefundedAmount, string? PaymentSource, IReadOnlyList<RefundResponse> Refunds);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total, string Currency,
    string FulfilmentStatus, IReadOnlyList<OrderItemResponse> Items, PaymentResponse Payment);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string LastDigits,
    string Expiry, DateTimeOffset CreatedAt);

public static class PaymentResponseMapper
{
    public static OrderResponse Order(Order order) => new(order.Id, order.OrderDate, order.Total(), order.Currency,
        order.FulfilmentStatus.ToString(), order.OrderItems.Select(x => new OrderItemResponse(
            x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        new PaymentResponse(order.PaymentStatus.ToString(), order.PayPalOrderId, order.AuthorizationId,
            order.AuthorizationStatus, order.AuthorizedAmount, order.AuthorizationExpiresAt, order.CaptureId,
            order.CaptureStatus, order.CapturedAmount, order.PayPalFee, order.NetAmount, order.RefundedAmount,
            order.PaymentSourceDescription, order.Refunds.Select(Refund).ToList()));

    public static RefundResponse Refund(PaymentRefund refund) => new(refund.PayPalRefundId, refund.Status,
        refund.Amount, refund.PayPalFee, refund.NetAmount, refund.CreatedAt);

    public static PaymentMethodResponse Method(PaymentMethod method) => new(method.Id, method.Brand,
        method.LastDigits, method.Expiry, method.CreatedAt);
}
