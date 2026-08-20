using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public List<OrderItemResponse> Items { get; set; } = new();
    public OrderPaymentResponse? Payment { get; set; }
    public List<OrderRefundResponse> Refunds { get; set; } = new();

    public static OrderResponse From(Order order, string currency)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = MoneyFormat.ToCents(order.Total()),
            Currency = order.Payment?.Currency ?? currency,
            OrderDate = order.OrderDate,
            BuyerId = order.BuyerId,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = order.Payment is null ? null : new OrderPaymentResponse
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpiration = order.Payment.AuthorizationExpiration,
                AuthorizedAmount = order.Payment.AuthorizedAmount,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PayPalFee = order.Payment.PayPalFee,
                NetProceeds = order.Payment.NetProceeds,
                Currency = order.Payment.Currency
            },
            Refunds = order.Refunds.Select(r => new OrderRefundResponse
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class OrderPaymentResponse
{
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetProceeds { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class OrderRefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public OrderResponse? Order { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class PaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodResponse From(SavedPaymentMethod method)
    {
        return new PaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            LastDigits = method.LastDigits,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName
        };
    }
}

public class PaymentMethodListResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public static class CardPaymentMapper
{
    public static CardDetails ToCardDetails(CardPaymentRequest card)
    {
        return new CardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            card.BillingAddress is null
                ? null
                : new CardBillingAddress(
                    card.BillingAddress.AddressLine1,
                    card.BillingAddress.AddressLine2,
                    card.BillingAddress.AdminArea1,
                    card.BillingAddress.AdminArea2,
                    card.BillingAddress.PostalCode,
                    card.BillingAddress.CountryCode));
    }
}
