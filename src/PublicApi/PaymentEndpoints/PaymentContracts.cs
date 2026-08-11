using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Request payloads (client-supplied). Identity is never taken from the body. ----

public record OrderItemRequest(int CatalogItemId, int Quantity);

public record AddressRequest(string Street, string City, string State, string Country, string ZipCode);

public record BillingAddressRequest(
    string AddressLine1, string? AddressLine2, string City, string? State, string PostalCode, string CountryCode);

/// <summary>Raw card details for a one-off payment or to vault. Never stored, never logged.</summary>
public record CardRequest(
    string Number, string Expiry, string SecurityCode, string CardholderName, BillingAddressRequest? BillingAddress);

public record PlaceOrderRequest(List<OrderItemRequest> Items, AddressRequest? ShipToAddress);

public record PayOrderRequest(CardRequest? Card, int? SavedPaymentMethodId);

public record RefundOrderRequest(decimal? Amount, string? IdempotencyKey);

public record SavePaymentMethodRequest(CardRequest Card, string? Alias);

// ---- Response payloads ----

public record RefundDto(string RefundId, decimal Amount, string Status);

public record PaymentStateDto(
    int OrderId,
    string Status,
    decimal Amount,
    string Currency,
    string? PayPalOrderId,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundDto> Refunds);

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderWithPaymentDto(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderItemDto> Items,
    PaymentStateDto? Payment);

public record SavedCardDto(int PaymentMethodId, string? Brand, string? Last4, string? Expiry, string? Alias,
    DateTimeOffset CreatedAt);

// ---- Top-level response envelopes (creating responses expose their id at the top level) ----

public record PlaceOrderResponse(int OrderId, PaymentStateDto? Payment);

public record OrderPaymentResponse(int OrderId, PaymentStateDto Payment);

public record RefundResponse(string RefundId, RefundDto Refund, PaymentStateDto Payment);

public record SavePaymentMethodResponse(int PaymentMethodId, SavedCardDto Card);

public record MyOrdersResponse(IReadOnlyList<OrderWithPaymentDto> Orders);

public record PaymentMethodsResponse(IReadOnlyList<SavedCardDto> PaymentMethods);

// ---- Commands that fold a route parameter together with a body ----

public record PayOrderCommand(int OrderId, PayOrderRequest? Body);

public record RefundOrderCommand(int OrderId, RefundOrderRequest? Body);

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

/// <summary>Maps domain entities onto safe API response shapes.</summary>
public static class PaymentMapper
{
    public static PaymentStateDto? ToDto(Payment? payment)
    {
        if (payment is null)
        {
            return null;
        }

        return new PaymentStateDto(
            payment.OrderId,
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency,
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.TotalRefunded,
            payment.RefundableRemaining,
            payment.Refunds.Select(r => new RefundDto(r.RefundId, r.Amount, r.Status)).ToList());
    }

    public static OrderWithPaymentDto ToDto(OrderPaymentView view)
    {
        var items = view.Order.OrderItems
            .Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        return new OrderWithPaymentDto(
            view.Order.Id,
            view.Order.OrderDate,
            view.Order.Total(),
            items,
            ToDto(view.Payment));
    }

    public static SavedCardDto ToDto(PaymentMethod method) =>
        new SavedCardDto(method.Id, method.Brand, method.Last4, method.Expiry, method.Alias, method.CreatedAt);
}

/// <summary>Maps request payloads onto the application's transient card/instruction types.</summary>
public static class PaymentRequestMapper
{
    public static CardDetails ToCardDetails(CardRequest card) => new CardDetails(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.CardholderName,
        card.BillingAddress is null
            ? null
            : new BillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));

    public static Address ToAddress(AddressRequest? address) => address is null
        // The payment flow does not require a real shipping address; use a placeholder when none is given.
        ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
        : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
}

public static class CallerIdentity
{
    /// <summary>The signed-in shopper, taken from the JWT (never from the request body).</summary>
    public static string BuyerId(HttpContext context)
    {
        var name = context.User?.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new PaymentException("The caller could not be identified from the token.", 401);
        }
        return name;
    }
}
