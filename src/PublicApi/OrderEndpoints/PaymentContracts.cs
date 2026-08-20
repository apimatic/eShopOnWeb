using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class PaymentEndpointHelpers
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentException("The caller is not authenticated.", System.Net.HttpStatusCode.Unauthorized);
        }

        return name;
    }

    public static OrderResponse ToResponse(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment.Currency,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = new PaymentResponse
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationCreatedAt = order.Payment.AuthorizationCreatedAt,
                AuthorizationExpiresAt = order.Payment.AuthorizationExpiresAt,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PaypalFee = order.Payment.PaypalFee,
                NetAmount = order.Payment.NetAmount,
                CardBrand = order.Payment.CardBrand,
                CardLast4 = order.Payment.CardLast4,
                RefundedAmount = order.RefundedTotal(),
                RemainingRefundable = order.RemainingRefundable()
            },
            Refunds = order.Refunds.Select(ToRefundResponse).ToList()
        };
    }

    public static RefundResponse ToRefundResponse(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency,
        CreatedAt = refund.CreatedAt
    };

    public static Address? ToAddress(AddressRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        return new Address(
            request.Street ?? "2211 N First Street",
            request.City ?? "San Jose",
            request.State ?? "CA",
            request.Country ?? "US",
            request.ZipCode ?? "95131");
    }

    public static CardPaymentDetails ToCard(CardRequest card) => new()
    {
        Number = card.Number ?? string.Empty,
        Expiry = card.Expiry ?? string.Empty,
        SecurityCode = card.SecurityCode ?? card.Cvv,
        Name = card.Name,
        BillingAddress = card.BillingAddress is null
            ? null
            : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1 ?? card.BillingAddress.Street,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.AdminArea2 ?? card.BillingAddress.City,
                AdminArea1 = card.BillingAddress.AdminArea1 ?? card.BillingAddress.State,
                PostalCode = card.BillingAddress.PostalCode ?? card.BillingAddress.ZipCode,
                CountryCode = card.BillingAddress.CountryCode ?? card.BillingAddress.Country
            }
    };
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? Cvv { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? BillingAddress { get; set; }
}

public class CardAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
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
    public List<RefundResponse> Refunds { get; set; } = new();
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
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
