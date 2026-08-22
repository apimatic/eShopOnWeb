using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public static class OrderEndpointHelpers
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        return user.Identity?.Name
               ?? throw new UnauthorizedAccessException("The caller identity is missing from the token.");
    }

    public static bool IsAdministrator(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static OrderDetailsDto ToDto(Order order)
    {
        return new OrderDetailsDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.PaymentCurrency,
            Payment = new OrderPaymentDto
            {
                PayPalOrderId = order.PayPalOrderId,
                PayPalOrderStatus = order.PayPalOrderStatus,
                AuthorizationId = order.AuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus,
                AuthorizationExpiration = order.AuthorizationExpiration,
                CaptureId = order.CaptureId,
                CaptureStatus = order.CaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PaypalFee = order.PaypalFee,
                NetAmount = order.NetAmount,
                RemainingRefundableAmount = order.RemainingRefundableAmount(),
                Refunds = order.Refunds.Select(r => new OrderRefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Status = r.Status,
                    Amount = r.Amount,
                    Currency = r.Currency,
                    IdempotencyKey = r.IdempotencyKey,
                    CreatedAt = r.CreatedAt
                }).ToList()
            },
            Items = order.OrderItems.Select(i => new OrderLineDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList()
        };
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
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
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string CountryCode { get; set; } = "US";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDetailsDto? Order { get; set; }
}

public class OrderActionResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDetailsDto? Order { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public OrderRefundDto? Refund { get; set; }
    public OrderDetailsDto? Order { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderDetailsDto> Orders { get; set; } = new();
}

public class OrderDetailsDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public OrderPaymentDto Payment { get; set; } = new();
    public List<OrderLineDto> Items { get; set; } = new();
}

public class OrderPaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public static class CardRequestMapping
{
    public static CardPaymentSource ToPaymentSource(CardRequest card)
    {
        CardBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.CountryCode,
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.PostalCode);
        }

        return new CardPaymentSource(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            billing);
    }
}
