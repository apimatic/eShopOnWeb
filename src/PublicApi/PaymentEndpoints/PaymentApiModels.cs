using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)] public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class CreateOrderLineRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = null!;
    [Required, MaxLength(100)] public string City { get; set; } = null!;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = null!;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = null!;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    [Range(1, int.MaxValue)] public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardRequest Card { get; set; } = null!;
}

public sealed class CardRequest
{
    [Required, RegularExpression("^(?:[0-9] ?){12,19}$")] public string Number { get; set; } = null!;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; set; } = null!;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; set; } = null!;
    [Required, StringLength(300, MinimumLength = 2)] public string Name { get; set; } = null!;
    [Required] public BillingAddressRequest BillingAddress { get; set; } = null!;
}

public sealed class BillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = null!;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = null!;
    [Required, MaxLength(120)] public string State { get; set; } = null!;
    [Required, MaxLength(60)] public string PostalCode { get; set; } = null!;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; set; } = null!;
}

public sealed class RefundOrderRequest
{
    [Range(typeof(decimal), "0.01", "9999999999999999")] public decimal? Amount { get; set; }
    [StringLength(200, MinimumLength = 1)] public string? IdempotencyKey { get; set; }
}

public sealed record OrderLineResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundResponse(string RefundId, decimal Amount, string Status, DateTimeOffset CreatedAt);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string PaymentStatus,
    string FulfilmentStatus,
    string? Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? MerchantNetAmount,
    decimal RefundedAmount,
    IReadOnlyCollection<OrderLineResponse> Items,
    IReadOnlyCollection<RefundResponse> Refunds)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.OrderDate,
        order.Total(),
        order.PaymentStatus.ToString(),
        order.FulfilmentStatus.ToString(),
        order.PaymentCurrency,
        order.PayPalOrderId,
        order.PayPalAuthorizationId,
        order.PayPalAuthorizationStatus,
        order.AuthorizationExpiresAt,
        order.PayPalCaptureId,
        order.PayPalCaptureStatus,
        order.CapturedAmount,
        order.PayPalFee,
        order.MerchantNetAmount,
        order.RefundedAmount,
        order.OrderItems.Select(item => new OrderLineResponse(item.ItemOrdered.CatalogItemId,
            item.ItemOrdered.ProductName, item.UnitPrice, item.Units)).ToArray(),
        order.Refunds.Select(refund => new RefundResponse(refund.PayPalRefundId, refund.Amount,
            refund.Status, refund.CreatedAt)).ToArray());
}

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string LastDigits,
    string Expiry,
    DateTimeOffset CreatedAt)
{
    public static PaymentMethodResponse From(PaymentMethod method) =>
        new(method.Id, method.Brand, method.LastDigits, method.Expiry, method.CreatedAt);
}

internal static class PaymentApiMapping
{
    internal static CardData ToData(this CardRequest card) => new(
        card.Number.Replace(" ", string.Empty, StringComparison.Ordinal), card.Expiry,
        card.SecurityCode, card.Name,
        new BillingAddressData(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
            card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));

    internal static Address ToAddress(this ShippingAddressRequest? address) => address is null
        ? new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied")
        : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
}
