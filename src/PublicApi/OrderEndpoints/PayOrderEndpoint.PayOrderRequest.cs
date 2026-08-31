using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods) to pay with.</summary>
    public int? PaymentMethodId { get; set; }
}

public class CardRequest
{
    [Required]
    [RegularExpression("^[0-9]{13,19}$")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry in YYYY-MM format.</summary>
    [Required]
    [RegularExpression("^[0-9]{4}-[0-9]{2}$")]
    public string Expiry { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[0-9]{3,4}$")]
    public string SecurityCode { get; set; } = string.Empty;

    public string? Name { get; set; }

    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }

    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
