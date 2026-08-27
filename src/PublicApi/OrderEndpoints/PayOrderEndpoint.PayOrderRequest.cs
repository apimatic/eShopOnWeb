namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>
    /// Card details for a one-off payment. Ignored when SavedCardId is set.
    /// Never persisted or logged.
    /// </summary>
    public CardRequest? Card { get; set; }

    /// <summary>
    /// Id of one of the caller's saved cards (from POST /api/payment-methods).
    /// </summary>
    public int? SavedCardId { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}
