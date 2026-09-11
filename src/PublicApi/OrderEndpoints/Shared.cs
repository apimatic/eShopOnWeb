using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Resolves the shopper (buyer) identity from the JWT the caller presents.</summary>
public static class CallerIdentity
{
    public static string GetBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("unique_name");
        if (string.IsNullOrEmpty(buyerId))
        {
            throw new NotFoundException("The caller's identity could not be determined from the token.");
        }
        return buyerId;
    }
}

/// <summary>Safe, shopper-facing view of a payment's state. Never carries full card details.</summary>
public class PaymentStateDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? AuthorizationExpiresAt { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }

    public decimal TotalRefunded { get; set; }
    public decimal RefundableRemaining { get; set; }

    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public List<RefundDto> Refunds { get; set; } = new();

    public static PaymentStateDto From(Payment payment)
    {
        return new PaymentStateDto
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            CurrencyCode = payment.CurrencyCode,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt?.ToString("o"),
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            TotalRefunded = payment.TotalRefunded,
            RefundableRemaining = payment.RefundableRemaining,
            CardBrand = payment.CardBrand,
            CardLast4 = payment.CardLast4,
            Refunds = payment.Refunds
                .Select(r => new RefundDto
                {
                    RefundId = r.Id,
                    PayPalRefundId = r.PayPalRefundId,
                    Amount = r.Amount,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt.ToString("o")
                })
                .ToList()
        };
    }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string? PayPalRefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>Billing address for a one-off card payment or a saved card.</summary>
public class CardBillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>Raw card details a shopper supplies. Never stored; passed straight to PayPal.</summary>
public class CardInputDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressDto? BillingAddress { get; set; }
}
