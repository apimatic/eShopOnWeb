using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
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

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string AdminArea1 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderResponse : BaseResponse
{
    public OrderResponse(Guid correlationId) : base(correlationId) { }
    public OrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public OrderPaymentDto Payment { get; set; } = new();
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();
}

public class OrderListResponse : BaseResponse
{
    public OrderListResponse(Guid correlationId) : base(correlationId) { }
    public OrderListResponse() { }

    public List<OrderResponse> Orders { get; set; } = new();
}

public class RefundResponse : BaseResponse
{
    public RefundResponse(Guid correlationId) : base(correlationId) { }
    public RefundResponse() { }

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class OrderPaymentDto
{
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class OrderRefundDto
{
    public int Id { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public static class OrderResponseMapper
{
    public static OrderResponse ToResponse(Order order, Guid correlationId)
    {
        var response = new OrderResponse(correlationId)
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Payment = new OrderPaymentDto
            {
                Currency = order.Payment.Currency,
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizationExpiration = order.Payment.AuthorizationExpiration,
                AuthorizedAmount = order.Payment.AuthorizedAmount,
                CaptureId = order.Payment.CaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PaypalFee = order.Payment.PaypalFee,
                NetAmount = order.Payment.NetAmount
            },
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = order.Refunds.Select(r => new OrderRefundDto
            {
                Id = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Status = r.Status,
                Amount = r.Amount,
                Currency = r.Currency
            }).ToList()
        };

        return response;
    }
}
