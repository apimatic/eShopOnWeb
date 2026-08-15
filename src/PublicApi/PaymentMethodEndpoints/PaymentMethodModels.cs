using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>A saved card, described safely enough to recognise it — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? CardBrand { get; set; }
    public string? Last4 { get; set; }
    public string? ExpiryMonth { get; set; }
    public string? ExpiryYear { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static PaymentMethodDto FromEntity(PaymentMethod paymentMethod) => new()
    {
        PaymentMethodId = paymentMethod.Id,
        Alias = paymentMethod.Alias,
        CardBrand = paymentMethod.CardBrand,
        Last4 = paymentMethod.Last4,
        ExpiryMonth = paymentMethod.ExpiryMonth,
        ExpiryYear = paymentMethod.ExpiryYear,
        CreatedDate = paymentMethod.CreatedDate
    };
}

/// <summary>Body for saving a card. The card is vaulted at PayPal; this app never stores the number.</summary>
public class SavePaymentMethodRequest
{
    public CardDto Card { get; set; } = default!;
    public string? Alias { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? BuyerId { get; set; }
}

/// <summary>Response for a saved card, carrying its identifier as a top-level field.</summary>
public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = default!;
}
