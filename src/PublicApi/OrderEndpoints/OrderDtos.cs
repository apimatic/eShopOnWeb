using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>
/// One-off card details. Transmitted to PayPal only; never persisted or logged.
/// </summary>
public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Card expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }

    public GatewayCardDetails ToGatewayCard()
    {
        return new GatewayCardDetails
        {
            Number = Number,
            Expiry = Expiry,
            SecurityCode = SecurityCode,
            CardholderName = CardholderName,
            BillingAddress = BillingAddress is null ? null : new GatewayAddress
            {
                AddressLine1 = BillingAddress.AddressLine1,
                AddressLine2 = BillingAddress.AddressLine2,
                City = BillingAddress.City,
                State = BillingAddress.State,
                PostalCode = BillingAddress.PostalCode,
                CountryCode = BillingAddress.CountryCode
            }
        };
    }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new List<RefundDto>();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderSummaryItemDto> Items { get; set; } = new List<OrderSummaryItemDto>();
    public PaymentDto? Payment { get; set; }
}

public class OrderSummaryItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
