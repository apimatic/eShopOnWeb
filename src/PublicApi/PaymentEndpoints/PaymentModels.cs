using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----------------------------------------------------------------- Card / address input

/// <summary>
/// Raw card details supplied by the shopper. Passed straight to PayPal; never stored by this
/// app. <paramref name="Expiry"/> is "YYYY-MM" (e.g. "2030-01").
/// </summary>
public record CardInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    BillingAddressInput BillingAddress);

public record BillingAddressInput(
    string AddressLine1,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

// ----------------------------------------------------------------- Requests

public record OrderLineInput(int CatalogItemId, int Quantity);

public record ShipToAddressInput(string Street, string City, string State, string Country, string ZipCode);

public record CreateOrderRequest(
    List<OrderLineInput> Items,
    ShipToAddressInput? ShipToAddress);

/// <summary>Pay an order with either a one-off <see cref="Card"/> or a <see cref="SavedCardId"/>.</summary>
public record PayOrderRequest(CardInput? Card, int? SavedCardId);

public record RefundRequest(decimal? Amount, string IdempotencyKey);

public record SavePaymentMethodRequest(CardInput Card, string? Alias);

// ----------------------------------------------------------------- Responses

public record CreateOrderResponse(int OrderId, string Status, decimal Total, string Currency,
    IReadOnlyList<OrderItemDto> Items);

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record RefundDto(int RefundId, string PayPalRefundId, decimal Amount, string CurrencyCode, string Status);

public record PaymentDto(
    string PayPalOrderId,
    string CurrencyCode,
    decimal AuthorizedAmount,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    string? CardDescriptor,
    IReadOnlyList<RefundDto> Refunds);

public record OrderDto(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items,
    PaymentDto? Payment);

public record PaymentMethodDto(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? Alias);

// ----------------------------------------------------------------- Mapping

public static class PaymentMappings
{
    public static CardDetails ToCardDetails(this CardInput input) => new(
        Number: input.Number,
        Expiry: input.Expiry,
        SecurityCode: input.SecurityCode,
        Name: input.Name,
        BillingAddress: new CardBillingAddress(
            AddressLine1: input.BillingAddress.AddressLine1,
            AdminArea2: input.BillingAddress.City,
            AdminArea1: input.BillingAddress.State,
            PostalCode: input.BillingAddress.PostalCode,
            CountryCode: input.BillingAddress.CountryCode));

    public static OrderItemDto ToDto(this OrderItem item) =>
        new(item.ItemOrdered.CatalogItemId, item.ItemOrdered.ProductName, item.UnitPrice, item.Units);

    public static RefundDto ToDto(this Refund refund) =>
        new(refund.Id, refund.PayPalRefundId, refund.Amount, refund.CurrencyCode, refund.Status);

    public static PaymentDto? ToDto(this Payment? payment)
    {
        if (payment is null) return null;
        return new PaymentDto(
            payment.PayPalOrderId,
            payment.CurrencyCode,
            payment.AuthorizedAmount,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.AuthorizationExpiresAt,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.TotalRefunded,
            payment.RefundableRemaining,
            payment.CardDescriptor,
            payment.Refunds.Select(r => r.ToDto()).ToList());
    }

    public static OrderDto ToDto(this Order order) =>
        new(order.Id,
            order.Status.ToString(),
            order.OrderDate,
            order.Total(),
            order.OrderItems.Select(i => i.ToDto()).ToList(),
            order.Payment.ToDto());

    public static PaymentMethodDto ToDto(this PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.Alias);
}
