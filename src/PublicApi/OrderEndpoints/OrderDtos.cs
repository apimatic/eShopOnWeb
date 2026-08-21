using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public AddressDto? ShipTo { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public decimal RefundableRemaining { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PaymentDto
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
    public string? InvoiceId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class OrderDtoMapper
{
    public static OrderDto ToDto(Order order, string? fallbackCurrency = null)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment.Currency ?? fallbackCurrency,
            ShipTo = order.ShipToAddress is null ? null : new AddressDto
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = ToPaymentDto(order.Payment),
            Refunds = order.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                IdempotencyKey = r.IdempotencyKey
            }).ToList(),
            RefundableRemaining = order.RefundableRemaining()
        };
    }

    private static PaymentDto? ToPaymentDto(OrderPayment? payment)
    {
        if (payment is null)
        {
            return null;
        }

        return new PaymentDto
        {
            PayPalOrderId = payment.PayPalOrderId,
            PayPalOrderStatus = payment.PayPalOrderStatus,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiration = payment.AuthorizationExpiration,
            AuthorizedAmount = payment.AuthorizedAmount,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PaypalFee = payment.PaypalFee,
            NetAmount = payment.NetAmount,
            Currency = payment.Currency,
            InvoiceId = payment.InvoiceId,
            SavedPaymentMethodId = payment.SavedPaymentMethodId
        };
    }
}

public static class EndpointUser
{
    public static string RequireBuyerId(HttpContext http)
    {
        var name = http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApplicationCore.Exceptions.PaymentException("A signed-in shopper is required.", 401, "UNAUTHENTICATED");
        }

        return name;
    }
}
