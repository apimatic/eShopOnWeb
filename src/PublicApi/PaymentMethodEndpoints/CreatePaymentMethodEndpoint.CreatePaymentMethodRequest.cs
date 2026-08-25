namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>Set by the endpoint from the caller's JWT - ignore any value supplied by the client.</summary>
    public string BuyerId { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    /// <summary>"YYYY-MM"</summary>
    public string ExpiryYearMonth { get; set; } = string.Empty;

    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>An optional shopper-chosen label, e.g. "My Visa".</summary>
    public string? Alias { get; set; }
}
