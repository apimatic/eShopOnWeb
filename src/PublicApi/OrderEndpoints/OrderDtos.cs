using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Cvc { get; set; }
    public string? Cvv { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }

    public CardPaymentSource ToCardPaymentSource()
    {
        return new CardPaymentSource(
            Number,
            Expiry,
            SecurityCode ?? Cvc ?? Cvv,
            Name,
            BillingAddress == null
                ? null
                : new CardBillingAddress(
                    BillingAddress.AddressLine1 ?? BillingAddress.Street,
                    BillingAddress.AddressLine2,
                    BillingAddress.AdminArea2 ?? BillingAddress.City,
                    BillingAddress.AdminArea1 ?? BillingAddress.State,
                    BillingAddress.PostalCode ?? BillingAddress.ZipCode,
                    BillingAddress.CountryCode ?? BillingAddress.Country));
    }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Country { get; set; }
}

public class CreateRefundRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class CreateRefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public OrderPaymentDto Payment { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderPaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<OrderRefundDto> Refunds { get; set; } = new();
    public decimal RemainingRefundable { get; set; }
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Currency = order.Currency,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = new OrderPaymentDto
            {
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.PayPalAuthorizationId,
                AuthorizationStatus = order.PayPalAuthorizationStatus,
                AuthorizationExpiration = order.PayPalAuthorizationExpiration,
                CaptureId = order.PayPalCaptureId,
                CaptureStatus = order.PayPalCaptureStatus,
                CapturedAmount = order.CapturedAmount,
                PayPalFee = order.PayPalFee,
                NetAmount = order.NetAmount,
                RemainingRefundable = order.RemainingRefundable(),
                Refunds = order.Refunds.Select(r => new OrderRefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Status = r.Status,
                    Amount = r.Amount,
                    IdempotencyKey = r.IdempotencyKey
                }).ToList()
            }
        };
    }

    public static Address? ToAddress(ShipToAddressRequest? request)
    {
        if (request == null)
        {
            return null;
        }

        return new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
    }
}
