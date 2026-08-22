using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

internal static class OrderDtoMapper
{
    public static OrderDto Map(Order order, OrderPayment? payment, string currency)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.Currency ?? currency,
            Status = payment?.Status.ToString() ?? "AwaitingPayment",
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = payment is null ? null : MapPayment(payment)
        };
    }

    public static PaymentDto MapPayment(OrderPayment payment)
    {
        return new PaymentDto
        {
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiration = payment.AuthorizationExpiration,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PaypalFee = payment.PaypalFee,
            NetProceeds = payment.NetProceeds,
            RemainingRefundable = payment.RemainingRefundable,
            Refunds = payment.Refunds.Select(MapRefund).ToList()
        };
    }

    public static RefundDto MapRefund(PaymentRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Status = refund.Status
        };
    }

    public static CardPaymentRequest? ToCard(CardRequest? card)
    {
        if (card is null)
        {
            return null;
        }

        return new CardPaymentRequest
        {
            Number = card.Number ?? string.Empty,
            Expiry = card.Expiry ?? string.Empty,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new PayPalBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode ?? "US"
                }
        };
    }
}

public class CardRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}
