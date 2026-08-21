using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public OrderAddressRequest? ShipTo { get; set; }
    public string? BuyerId { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class OrderResponse : BaseResponse
{
    public OrderResponse(Guid correlationId) : base(correlationId) { }
    public OrderResponse() { }

    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentStateResponse? Payment { get; set; }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class PaymentStateResponse
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundableAmount { get; set; }
    public List<RefundStateResponse> Refunds { get; set; } = new();
}

public class RefundStateResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<OrderResponse> Orders { get; set; } = new();
}

public static class OrderResponseMapper
{
    public static OrderResponse ToResponse(Order order, string currency, Guid? correlationId = null)
    {
        var response = correlationId.HasValue ? new OrderResponse(correlationId.Value) : new OrderResponse();
        response.OrderId = order.Id;
        response.BuyerId = order.BuyerId;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.Currency = order.Payment?.Currency ?? currency;
        response.OrderDate = order.OrderDate;
        response.Items = order.OrderItems.Select(i => new OrderItemResponse
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Quantity = i.Units
        }).ToList();

        if (order.Payment != null)
        {
            response.Payment = new PaymentStateResponse
            {
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpirationTime = order.Payment.AuthorizationExpirationTime,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = string.IsNullOrEmpty(order.Payment.CaptureId) ? null : order.Payment.CapturedAmount,
                PayPalFee = string.IsNullOrEmpty(order.Payment.CaptureId) ? null : order.Payment.PayPalFee,
                NetAmount = string.IsNullOrEmpty(order.Payment.CaptureId) ? null : order.Payment.NetAmount,
                RefundedAmount = order.RefundedTotal(),
                RemainingRefundableAmount = order.RemainingRefundableAmount(),
                Refunds = order.Refunds.Select(r => new RefundStateResponse
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status
                }).ToList()
            };
        }

        return response;
    }

    public static CardPaymentSource? ToCardSource(CardDetailsRequest? card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.Number))
        {
            return null;
        }

        CardBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) ? "US" : card.BillingAddress.CountryCode);
        }

        return new CardPaymentSource(
            card.Number,
            card.Expiry ?? string.Empty,
            card.SecurityCode,
            card.Name,
            billing);
    }
}
