namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Populated from the route.</summary>
    public int OrderId { get; set; }

    /// <summary>One-off card details. Mutually exclusive with PaymentMethodId.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (POST /api/payment-methods).</summary>
    public int? PaymentMethodId { get; set; }
}

/// <summary>
/// Full card details, used only for the duration of the PayPal call.
/// Never persisted, never logged.
/// </summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>City, town or village.</summary>
    public string? AdminArea2 { get; set; }

    /// <summary>State, province or equivalent ISO-3166-2 subdivision.</summary>
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}
