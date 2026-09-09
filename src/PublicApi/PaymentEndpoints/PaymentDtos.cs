using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details supplied by a shopper for a one-off payment or to save. Never persisted or logged.</summary>
public class CardRequestDto
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM form.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class OrderLineDto
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

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An order together with the payment state that follows it.</summary>
public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public System.DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? PaymentInstrumentDescription { get; set; }
    public decimal RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

/// <summary>Mapping and current-user helpers shared by the payment endpoints.</summary>
public static class PaymentMapping
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(name))
        {
            throw new NotFoundException("The caller could not be identified.");
        }
        return name;
    }

    public static PayPalCardDetails ToGatewayCard(this CardRequestDto card) =>
        new(card.Number, card.Expiry, card.SecurityCode, card.CardholderName,
            card.BillingAddress is null
                ? null
                : new PayPalBillingAddress(
                    card.BillingAddress.AddressLine1,
                    card.BillingAddress.AddressLine2,
                    card.BillingAddress.City,
                    card.BillingAddress.State,
                    card.BillingAddress.PostalCode,
                    card.BillingAddress.CountryCode));

    public static OrderPaymentDto ToDto(this OrderPaymentView view) => new()
    {
        OrderId = view.OrderId,
        OrderDate = view.OrderDate,
        Status = view.Status,
        Amount = view.Amount,
        CurrencyCode = view.CurrencyCode,
        PayPalOrderId = view.PayPalOrderId,
        AuthorizationId = view.AuthorizationId,
        AuthorizationStatus = view.AuthorizationStatus,
        AuthorizationExpiresAt = view.AuthorizationExpiresAt,
        CaptureId = view.CaptureId,
        CaptureStatus = view.CaptureStatus,
        CapturedAmount = view.CapturedAmount,
        PayPalFee = view.PayPalFee,
        NetAmount = view.NetAmount,
        PaymentInstrumentDescription = view.PaymentInstrumentDescription,
        RefundableAmount = view.RefundableAmount,
        Refunds = view.Refunds.Select(r => new RefundDto
        {
            RefundId = r.RefundId,
            PayPalRefundId = r.PayPalRefundId,
            Amount = r.Amount,
            CurrencyCode = r.CurrencyCode,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}
