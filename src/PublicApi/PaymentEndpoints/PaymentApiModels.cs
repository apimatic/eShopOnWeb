using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Helpers shared by the payment / saved-card endpoints.</summary>
public static class PaymentApi
{
    public const string OrdersTag = "OrderPaymentEndpoints";
    public const string PaymentMethodsTag = "PaymentMethodEndpoints";

    /// <summary>The caller's identity (username) taken from the JWT — used as the buyer id.</summary>
    public static string BuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name
            ?? throw new PaymentValidationException("The authenticated user has no identity name claim.");
}

// ---------------- Requests ----------------

/// <summary>Card details for a one-off payment or to save. Never persisted or logged in full.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM form, e.g. 2030-04.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    /// <summary>Two-letter ISO-3166-1 country code (required by PayPal).</summary>
    public string CountryCode { get; set; } = string.Empty;
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PayOrderRequest
{
    /// <summary>A one-off card. Provide this OR <see cref="PaymentMethodId"/>.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>A saved card id. Provide this OR <see cref="Card"/>.</summary>
    public int? PaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit for a full refund of the remaining balance.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class AddPaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
    public string? Alias { get; set; }
}

// ---------------- Responses ----------------

public record OrderResponse
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateTimeOffset OrderDate { get; init; }
    public IReadOnlyList<OrderItemResponse> Items { get; init; } = Array.Empty<OrderItemResponse>();
    public PaymentResponse? Payment { get; init; }
}

public record OrderItemResponse
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

public record PaymentResponse
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public AuthorizationResponse? Authorization { get; init; }
    public CaptureResponse? Capture { get; init; }
    public IReadOnlyList<RefundResponse> Refunds { get; init; } = Array.Empty<RefundResponse>();
    public decimal TotalRefunded { get; init; }
    public decimal? RefundableRemaining { get; init; }
}

public record AuthorizationResponse
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record CaptureResponse
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public decimal PayPalFee { get; init; }
    public decimal NetAmount { get; init; }
}

public record RefundResponse
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
}

public record PaymentMethodResponse
{
    public int PaymentMethodId { get; init; }
    public string? Brand { get; init; }
    public string? Last4 { get; init; }
    public string? Expiry { get; init; }
    public string? Alias { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

// ---------------- Mapping ----------------

public static class PaymentApiMapper
{
    public static PayPalCardDetails ToCardDetails(CardRequest card)
    {
        PayPalBillingAddress? billing = null;
        if (card.BillingAddress is not null)
        {
            var a = card.BillingAddress;
            billing = new PayPalBillingAddress(a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode, a.CountryCode);
        }

        return new PayPalCardDetails(
            Number: card.Number,
            Expiry: card.Expiry,
            SecurityCode: card.SecurityCode,
            Name: card.Name,
            BillingAddress: billing);
    }

    public static OrderResponse ToOrderResponse(Order order, string currency)
    {
        var payment = order.Payment;
        PaymentResponse? paymentResponse = null;
        if (payment is not null)
        {
            paymentResponse = new PaymentResponse
            {
                PayPalOrderId = payment.PayPalOrderId,
                Currency = payment.Currency,
                Authorization = new AuthorizationResponse
                {
                    Id = payment.AuthorizationId,
                    Status = payment.AuthorizationStatus,
                    Amount = payment.AuthorizedAmount,
                    ExpiresAt = payment.AuthorizationExpiresAt
                },
                Capture = payment.IsCaptured
                    ? new CaptureResponse
                    {
                        Id = payment.CaptureId!,
                        Status = payment.CaptureStatus ?? string.Empty,
                        CapturedAmount = payment.CapturedAmount ?? 0m,
                        PayPalFee = payment.PayPalFee ?? 0m,
                        NetAmount = payment.NetAmount ?? 0m
                    }
                    : null,
                Refunds = payment.Refunds
                    .Select(r => new RefundResponse { RefundId = r.PayPalRefundId, Status = r.Status, Amount = r.Amount })
                    .ToList(),
                TotalRefunded = payment.TotalRefunded,
                RefundableRemaining = payment.IsCaptured ? payment.RefundableRemaining : null
            };
        }

        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems
                .Select(i => new OrderItemResponse
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                })
                .ToList(),
            Payment = paymentResponse
        };
    }

    public static PaymentMethodResponse ToPaymentMethodResponse(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        Alias = method.Alias,
        CreatedAt = method.CreatedAt
    };
}
