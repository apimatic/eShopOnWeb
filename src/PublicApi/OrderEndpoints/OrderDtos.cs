using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ShippingAddressDto? BillingAddress { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public CardDetailsDto? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class OrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public PaymentStateDto? Payment { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentStateDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public string? PaypalOrderId { get; set; }
    public string? PaypalOrderStatus { get; set; }
    public string? PaypalAuthorizationId { get; set; }
    public string? PaypalAuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? PaypalCaptureId { get; set; }
    public string? PaypalCaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PaypalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class RefundResponse : BaseResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string PaypalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class PaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

public class PaymentMethodListResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PaypalTransactionCount { get; set; }
    public int EshopOrderCount { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string Source { get; set; } = string.Empty;
    public string? PaypalTransactionId { get; set; }
    public int? OrderId { get; set; }
    public string? Match { get; set; }
    public string? PaypalStatus { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? PaypalAmount { get; set; }
    public decimal? OrderAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? PaypalDate { get; set; }
}

public static class OrderApiMapper
{
    public static OrderResponse ToResponse(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        Status = order.Status.ToString(),
        Total = order.Total(),
        OrderDate = order.OrderDate,
        Payment = order.Payment is null ? null : ToPayment(order.Payment),
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList()
    };

    public static PaymentStateDto ToPayment(OrderPayment payment) => new()
    {
        Currency = payment.Currency,
        AuthorizedAmount = payment.AuthorizedAmount,
        PaypalOrderId = payment.PaypalOrderId,
        PaypalOrderStatus = payment.PaypalOrderStatus,
        PaypalAuthorizationId = payment.PaypalAuthorizationId,
        PaypalAuthorizationStatus = payment.PaypalAuthorizationStatus,
        AuthorizationExpiration = payment.AuthorizationExpiration,
        PaypalCaptureId = payment.PaypalCaptureId,
        PaypalCaptureStatus = payment.PaypalCaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PaypalFee = payment.PaypalFee,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.RefundedAmount,
        RemainingRefundable = payment.RemainingRefundable,
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.Id,
            PaypalRefundId = r.PaypalRefundId,
            Status = r.Status,
            Amount = r.Amount,
            Currency = r.Currency
        }).ToList()
    };

    public static PaymentMethodResponse ToResponse(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.CardBrand,
        LastFourDigits = method.LastFourDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}
