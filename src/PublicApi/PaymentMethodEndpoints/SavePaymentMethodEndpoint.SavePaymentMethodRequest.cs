using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper via PayPal's vault. Card details are forwarded
/// to PayPal and never stored or logged by this application.
/// </summary>
public class SavePaymentMethodRequest : BaseRequest
{
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public SaveCardAddressRequest? BillingAddress { get; set; }
}

public class SaveCardAddressRequest
{
    [Required]
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}
