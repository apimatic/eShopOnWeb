namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    internal int OrderId { get; set; }
    internal string BuyerId { get; set; } = "";

    /// <summary>ID of a saved card (vault token). Mutually exclusive with Card.</summary>
    public string? SavedCardId { get; set; }

    /// <summary>One-off card payment details. Mutually exclusive with SavedCardId.</summary>
    public CardDetailsRequest? Card { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = "";
    public string ExpiryYear { get; set; } = "";
    public string ExpiryMonth { get; set; } = "";
    public string Cvv { get; set; } = "";
    public string CardholderName { get; set; } = "";
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string CountryCode { get; set; } = "US";
}
