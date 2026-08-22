using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShippingAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int? PaymentMethodId { get; set; }
    public CardDetailsDto? Card { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ShippingAddressDto? BillingAddress { get; set; }
}

public class CreateRefundRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new();
}

public class OrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
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
    public string? PaypalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class RefundResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class PaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class PaymentMethodListResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public static class OrderApiMapper
{
    public static OrderResponse ToResponse(Order order)
    {
        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = ToPayment(order)
        };
    }

    public static PaymentStateResponse ToPayment(Order order)
    {
        return new PaymentStateResponse
        {
            PaypalOrderId = order.PaypalOrderId,
            AuthorizationId = order.PaypalAuthorizationId,
            AuthorizationStatus = order.PaypalAuthorizationStatus,
            AuthorizationExpiration = order.AuthorizationExpiration,
            CaptureId = order.PaypalCaptureId,
            CaptureStatus = order.PaypalCaptureStatus,
            CapturedAmount = order.CapturedAmount,
            PaypalFee = order.PaypalFee,
            NetAmount = order.NetAmount,
            RemainingRefundable = order.RemainingRefundable(),
            Refunds = order.Refunds.Select(ToRefund).ToList()
        };
    }

    public static RefundResponse ToRefund(OrderRefund refund)
    {
        return new RefundResponse
        {
            RefundId = refund.PaypalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency
        };
    }

    public static PaymentMethodResponse ToPaymentMethod(SavedPaymentMethod method)
    {
        return new PaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            Last4 = method.Last4,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName
        };
    }

    public static Address? ToAddress(ShippingAddressDto? dto)
    {
        if (dto == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.Street)
            && string.IsNullOrWhiteSpace(dto.City)
            && string.IsNullOrWhiteSpace(dto.State)
            && string.IsNullOrWhiteSpace(dto.Country)
            && string.IsNullOrWhiteSpace(dto.ZipCode))
        {
            return null;
        }

        return new Address(
            dto.Street ?? string.Empty,
            dto.City ?? string.Empty,
            dto.State ?? string.Empty,
            dto.Country ?? "US",
            dto.ZipCode ?? string.Empty);
    }

    public static CardPayment ToCardPayment(CardDetailsDto card)
    {
        return new CardPayment
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }
}
