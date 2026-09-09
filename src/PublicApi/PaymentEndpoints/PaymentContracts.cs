using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ------------------------------------------------------------------ requests

public record CardRequest(
    string Number,
    string? Expiry,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? SecurityCode,
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? State,
    string? City,
    string? PostalCode,
    string? CountryCode)
{
    /// <summary>Normalise to PayPal's card shape ("YYYY-MM" expiry). Card data is forwarded to PayPal
    /// and never persisted or logged by this app.</summary>
    public CardDetails ToCardDetails()
    {
        if (string.IsNullOrWhiteSpace(Number))
        {
            throw new PaymentException("Card number is required.");
        }

        var expiry = NormaliseExpiry();
        return new CardDetails(
            Number.Replace(" ", string.Empty),
            expiry,
            SecurityCode,
            Name,
            AddressLine1,
            AddressLine2,
            State,      // admin_area_1
            City,       // admin_area_2
            PostalCode,
            CountryCode);
    }

    private string NormaliseExpiry()
    {
        if (!string.IsNullOrWhiteSpace(Expiry))
        {
            // Accept "YYYY-MM"; also tolerate "MM/YYYY" and "MM/YY".
            var e = Expiry.Trim();
            if (e.Length == 7 && e[4] == '-')
            {
                return e;
            }
            var parts = e.Split('/', '-');
            if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var y))
            {
                if (parts[0].Length == 4) // YYYY-MM already handled; here handle YYYY-M
                {
                    return $"{parts[0]}-{int.Parse(parts[1]):00}";
                }
                y = y < 100 ? 2000 + y : y;
                return $"{y:0000}-{m:00}";
            }
            throw new PaymentException("Card expiry must be in YYYY-MM format.");
        }

        if (ExpiryMonth is >= 1 and <= 12 && ExpiryYear is int yr)
        {
            var year = yr < 100 ? 2000 + yr : yr;
            return $"{year:0000}-{ExpiryMonth:00}";
        }

        throw new PaymentException("Provide the card expiry as 'expiry' (YYYY-MM) or expiryMonth + expiryYear.");
    }
}

public record OrderLineRequest(int CatalogItemId, int Units);

public record AddressRequest(string Street, string City, string State, string Country, string ZipCode);

public record CreateOrderRequest(List<OrderLineRequest> Items, AddressRequest? ShipToAddress);

public record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);

public record RefundRequest(decimal? Amount, string IdempotencyKey);

public record SavePaymentMethodRequest(CardRequest Card);

// ------------------------------------------------------------------ responses

public record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record RefundResponse(string RefundId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt);

public record PaymentResponse(
    string Status,
    string Currency,
    decimal AuthorizedAmount,
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId,
    string? CaptureStatus,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CapturedAt,
    decimal TotalRefunded,
    decimal RefundableRemaining,
    IReadOnlyList<RefundResponse> Refunds);

public record OrderResponse(
    int OrderId,
    string BuyerId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    string PaymentReference,
    AddressRequest ShipToAddress,
    IReadOnlyList<OrderItemResponse> Items,
    PaymentResponse? Payment)
{
    public static OrderResponse FromOrder(Order order)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemResponse(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName,
                i.UnitPrice, i.Units))
            .ToList();

        var address = order.ShipToAddress is { } a
            ? new AddressRequest(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new AddressRequest(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        PaymentResponse? payment = null;
        if (order.Payment is { } p)
        {
            payment = new PaymentResponse(
                p.Status.ToString(), p.Currency, p.AuthorizedAmount, p.PayPalOrderId, p.AuthorizationId,
                p.AuthorizationStatus, p.AuthorizationExpiresAt, p.CaptureId, p.CaptureStatus,
                p.CapturedAmount, p.PayPalFee, p.NetAmount, p.CapturedAt, p.TotalRefunded(),
                p.RefundableRemaining(),
                p.Refunds.Select(r => new RefundResponse(r.PayPalRefundId, r.Amount, r.Currency, r.Status,
                    r.CreatedAt)).ToList());
        }

        return new OrderResponse(order.Id, order.BuyerId, order.Status.ToString(), order.OrderDate,
            order.Total(), order.PaymentReference, address, items, payment);
    }
}

public record PaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string LastFourDigits,
    string Expiry,
    string? CardholderName,
    DateTimeOffset CreatedAt)
{
    public static PaymentMethodResponse From(PaymentMethod pm) => new(
        pm.Id, pm.Brand, pm.LastFourDigits, pm.Expiry, pm.CardholderName, pm.CreatedAt);
}
