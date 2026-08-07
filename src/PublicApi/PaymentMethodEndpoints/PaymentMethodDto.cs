using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// A safe description of a saved card — enough for the shopper to recognise it, never full card details.
/// </summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }

    /// <summary>Card network/brand, e.g. VISA (when known).</summary>
    public string? Brand { get; set; }

    /// <summary>Last four digits of the card.</summary>
    public string Last4 { get; set; } = string.Empty;

    /// <summary>Card expiry in <c>YYYY-MM</c> form (when known).</summary>
    public string? ExpiryMonthYear { get; set; }

    public string? CardholderName { get; set; }

    /// <summary>Optional shopper-supplied friendly name.</summary>
    public string? Alias { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto FromEntity(PaymentMethod pm) => new()
    {
        PaymentMethodId = pm.Id,
        Brand = pm.Brand,
        Last4 = pm.Last4,
        ExpiryMonthYear = pm.ExpiryMonthYear,
        CardholderName = pm.CardholderName,
        Alias = pm.Alias,
        CreatedDate = pm.CreatedDate
    };
}
