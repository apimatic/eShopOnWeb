using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class CheckoutHttp
{
    public static string BuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        return name;
    }

    public static OrderResponse ToResponse(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.PaymentStatus.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Currency,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                Quantity = i.Units,
                UnitPrice = i.UnitPrice
            }).ToList(),
            Payment = new PaymentResponse
            {
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.PayPalAuthorizationStatus,
                AuthorizationCreatedAt = order.AuthorizationCreatedAt,
                AuthorizationExpiresAt = order.AuthorizationExpiresAt,
                CaptureId = order.PayPalCaptureId,
                CaptureStatus = order.PayPalCaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PaypalFee = order.PayPalFee,
                NetAmount = order.NetAmount,
                RefundedAmount = order.RefundedAmount,
                RemainingRefundableAmount = order.RemainingRefundableAmount,
                Refunds = order.Refunds.Select(r => ToRefundResponse(r, order.Id)).ToList()
            }
        };
    }

    public static RefundResponse ToRefundResponse(PaymentRefund refund, int orderId) => new()
    {
        RefundId = refund.Id,
        OrderId = orderId,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency,
        IdempotencyKey = refund.IdempotencyKey,
        CreatedAt = refund.CreatedAt
    };

    public static RefundResponse ToRefundResponse(PaymentRefund refund) => ToRefundResponse(refund, 0);

    public static PaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse Payment { get; set; } = new();
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class PaymentResponse
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public static class CardDetailsMapping
{
    public static CardPaymentInput ToInput(CardDetailsRequest card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.Name,
        BillingAddress = card.BillingAddress is null
            ? null
            : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
    };
}
