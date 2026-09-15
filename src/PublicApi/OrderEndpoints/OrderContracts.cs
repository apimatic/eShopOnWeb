using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderApiRequest
{
    public List<PlaceOrderItemApiRequest> Items { get; set; } = new();
    public ShipToAddressApiRequest? ShipTo { get; set; }
}

public class PlaceOrderItemApiRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressApiRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PayOrderApiRequest
{
    public CardPaymentApiRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public class CardPaymentApiRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public CardBillingAddressApiRequest? BillingAddress { get; set; }

    public CardPaymentSource ToSource()
    {
        return new CardPaymentSource
        {
            Number = Number,
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            Name = Name,
            BillingAddress = BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = BillingAddress.AddressLine1,
                    AddressLine2 = BillingAddress.AddressLine2,
                    AdminArea2 = BillingAddress.AdminArea2,
                    AdminArea1 = BillingAddress.AdminArea1,
                    PostalCode = BillingAddress.PostalCode,
                    CountryCode = BillingAddress.CountryCode
                }
        };
    }
}

public class CardBillingAddressApiRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class RefundOrderApiRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }

    public static OrderResponse From(Order order)
    {
        var response = new OrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = order.Payment?.Currency,
            Payment = order.Payment is null ? null : PaymentResponse.From(order.Payment)
        };

        foreach (var item in order.OrderItems)
        {
            response.Items.Add(new OrderItemResponse
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            });
        }

        return response;
    }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentResponse
{
    public string? PayPalOrderId { get; set; }
    public string? PayPalOrderStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();

    public static PaymentResponse From(OrderPayment payment)
    {
        var response = new PaymentResponse
        {
            PayPalOrderId = payment.PayPalOrderId,
            PayPalOrderStatus = payment.PayPalOrderStatus,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            AuthorizedAmount = payment.AuthorizedAmount,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedTotal,
            RefundableRemaining = payment.RefundableRemaining
        };

        foreach (var refund in payment.Refunds)
        {
            response.Refunds.Add(RefundResponse.From(refund));
        }

        return response;
    }
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundResponse From(OrderRefund refund) => new()
    {
        RefundId = refund.Id,
        PayPalRefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency,
        CreatedAt = refund.CreatedAt
    };
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }

    public static PaymentMethodResponse From(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.LastDigits,
        Brand = method.Brand,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class PaymentMethodListResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
