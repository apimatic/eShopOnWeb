using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }

    public override string ToString() => "[redacted card]";
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;

    public override string ToString() => "[redacted billing address]";
}

public class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class OrderResponse
{
    public int OrderId { get; set; }
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
    public DateTimeOffset? AuthorizationExpiration { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();
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

public class RefundCreatedResponse : RefundResponse
{
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public static class OrderResponseMapper
{
    public static OrderResponse Map(Order order, string currency)
    {
        var payment = order.Payment;
        return new OrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = payment?.Currency ?? currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemResponse
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units
            }).ToList(),
            Payment = payment is null ? null : new PaymentStateResponse
            {
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                AuthorizationExpiration = payment.AuthorizationExpiration,
                CaptureId = payment.CaptureId,
                CaptureStatus = payment.CaptureStatus,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                CardLast4 = payment.CardLast4,
                CardBrand = payment.CardBrand,
                RefundedAmount = payment.RefundedAmount,
                RefundableRemaining = payment.RefundableRemaining,
                Refunds = payment.Refunds.Select(MapRefund).ToList()
            }
        };
    }

    public static RefundResponse MapRefund(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency,
        CreatedAt = refund.CreatedAt
    };

    public static CardPaymentDetails? ToCardDetails(CardRequest? card)
    {
        if (card is null)
        {
            return null;
        }

        return new CardPaymentDetails
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.AdminArea2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }

    public static Address ToAddress(AddressRequest? request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Street)
            || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.Country)
            || string.IsNullOrWhiteSpace(request.ZipCode))
        {
            return new Address("123 Main Street", "Seattle", "WA", "US", "98101");
        }

        return new Address(request.Street, request.City, request.State ?? string.Empty, request.Country, request.ZipCode);
    }

    public static PaymentMethodResponse Map(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName,
        DisplayName = method.Alias
    };
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string? DisplayName { get; set; }
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class SaveCardRequest
{
    public required CardRequest Card { get; set; }
}

public static class EndpointUser
{
    public static string BuyerId(HttpContext httpContext)
    {
        var name = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.OrderPaymentException(401, "The request is not authenticated.");
        }

        return name;
    }
}
