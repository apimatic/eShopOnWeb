namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Raw card details for a one-off payment. Never stored.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of a saved card (from POST api/payment-methods) to pay with instead.</summary>
    public int? SavedCardId { get; set; }
}

public class CardDetailsDto
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Format YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressDto? BillingAddress { get; set; }
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
