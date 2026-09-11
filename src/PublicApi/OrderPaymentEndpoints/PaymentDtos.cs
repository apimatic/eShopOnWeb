using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

// ---- Shared card / address request shapes -------------------------------------------------------

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? BillingAddress { get; set; }

    public CardDetails ToDomain() => new(
        Number,
        Expiry,
        SecurityCode,
        CardholderName,
        BillingAddress?.ToDomain());
}

public class AddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }

    public PayPalAddress ToDomain() => new(
        string.IsNullOrWhiteSpace(CountryCode) ? "US" : CountryCode!,
        AddressLine1, AddressLine2, AdminArea1, AdminArea2, PostalCode);
}

// ---- Place order --------------------------------------------------------------------------------

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

// ---- Pay ----------------------------------------------------------------------------------------

public class PayOrderRequest
{
    /// <summary>Populated from the route, not the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    public CardRequest? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}

// ---- Refund -------------------------------------------------------------------------------------

public class RefundOrderRequest
{
    /// <summary>Populated from the route, not the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund; omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. A repeat under the same key does not refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- Responses ----------------------------------------------------------------------------------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderLineResponse> Items { get; set; } = new();
}

public class OrderLineResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class RefundResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
}

public class AuthorizationDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class CaptureDto
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderPaymentResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public AuthorizationDto? Authorization { get; set; }
    public CaptureDto? Capture { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public string? FailureReason { get; set; }
}

public class MyOrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public OrderPaymentResponse Payment { get; set; } = new();
    public List<OrderLineResponse> Items { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderResponse> Orders { get; set; } = new();
}

// ---- Mapping ------------------------------------------------------------------------------------

public static class PaymentResponseMapper
{
    public static OrderPaymentResponse ToResponse(OrderPayment payment)
    {
        var response = new OrderPaymentResponse
        {
            OrderId = payment.OrderId,
            PaymentStatus = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.CurrencyCode,
            PayPalOrderId = payment.PayPalOrderId,
            TotalRefunded = payment.TotalRefunded(),
            RefundableRemaining = payment.RefundableRemaining(),
            FailureReason = payment.FailureReason,
            Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RefundDto { RefundId = r.RefundId, Amount = r.Amount, Status = r.Status, CreatedAt = r.CreatedAt })
                .ToList(),
        };

        if (!string.IsNullOrEmpty(payment.AuthorizationId))
        {
            response.Authorization = new AuthorizationDto
            {
                Id = payment.AuthorizationId!,
                Status = payment.AuthorizationStatus,
                ExpiresAt = payment.AuthorizationExpiresAt,
            };
        }

        if (!string.IsNullOrEmpty(payment.CaptureId))
        {
            response.Capture = new CaptureDto
            {
                Id = payment.CaptureId!,
                Status = payment.CaptureStatus,
                CapturedAmount = payment.CapturedAmount ?? 0m,
                PayPalFee = payment.PayPalFee ?? 0m,
                NetAmount = payment.NetAmount ?? 0m,
            };
        }

        return response;
    }

    public static List<OrderLineResponse> ToLines(Order order) => order.OrderItems
        .Select(i => new OrderLineResponse
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units,
        })
        .ToList();
}
