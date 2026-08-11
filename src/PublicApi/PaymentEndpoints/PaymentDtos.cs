using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---------- Shared request pieces ----------

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CardBillingAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class CardDto
{
    /// <summary>Full card number. Used transiently for this call only; never stored or logged.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in "YYYY-MM" form (e.g. "2030-01").</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public CardBillingAddressDto? BillingAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

// ---------- Requests ----------

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }
}

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>Amount to refund; omit to refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key so repeats never refund twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---------- Responses ----------

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public int OrderId { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class PaymentMethodDto
{
    public int Id { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderRefundDto
{
    public int RefundId { get; set; }
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public string? PayPalRefundId { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<MyOrderRefundDto> Refunds { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public bool InPayPal { get; set; }
    public bool InEShop { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalNotEShopCount { get; set; }
    public int InEShopNotPayPalCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> InPayPalNotEShop { get; set; } = new();
    public List<ReconciliationEntryDto> InEShopNotPayPal { get; set; } = new();
}

// ---------- Mapping helpers ----------

public static class PaymentMappings
{
    public static CardDetails ToCardDetails(this CardDto card) => new(
        card.Number,
        card.Expiry,
        card.SecurityCode,
        card.CardholderName,
        card.BillingAddress?.Line1,
        card.BillingAddress?.Line2,
        card.BillingAddress?.City,
        card.BillingAddress?.State,
        card.BillingAddress?.PostalCode,
        card.BillingAddress?.CountryCode);

    public static Address ToDomainAddress(this AddressDto? dto)
    {
        // The order model requires a ship-to address; supply a neutral placeholder when the caller
        // (an API-only payment flow) does not provide one.
        if (dto is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(dto.Street, dto.City, dto.State ?? "N/A", dto.Country, dto.ZipCode);
    }

    public static PaymentMethodDto ToDto(this SavedPaymentMethod pm) => new()
    {
        Id = pm.Id,
        CardBrand = pm.CardBrand,
        LastFourDigits = pm.LastFourDigits,
        Expiry = pm.CardExpiry,
        CardholderName = pm.CardholderName,
        CreatedAt = pm.CreatedAt
    };

    public static MyOrderDto ToDto(this OrderWithPayment ow)
    {
        var order = ow.Order;
        var payment = ow.Payment;
        return new MyOrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.Currency ?? string.Empty,
            PaymentStatus = payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationStatus = payment?.AuthorizationStatus,
            CaptureId = payment?.CaptureId,
            CapturedAmount = payment?.CapturedAmount,
            PayPalFee = payment?.PayPalFee,
            NetAmount = payment?.NetAmount,
            TotalRefunded = payment?.TotalRefunded ?? 0m,
            RefundableRemaining = payment?.RefundableAmount ?? 0m,
            Items = order.OrderItems.Select(i => new MyOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Refunds = payment?.Refunds.Select(r => new MyOrderRefundDto
            {
                RefundId = r.Id,
                Amount = r.Amount,
                Status = r.PayPalStatus,
                PayPalRefundId = r.PayPalRefundId
            }).ToList() ?? new List<MyOrderRefundDto>()
        };
    }

    public static ReconciliationEntryDto ToDto(this ReconciliationEntry e) => new()
    {
        TransactionId = e.TransactionId,
        Kind = e.Kind,
        Amount = e.Amount,
        Currency = e.Currency,
        OrderId = e.OrderId,
        InPayPal = e.InPayPal,
        InEShop = e.InEShop
    };
}
