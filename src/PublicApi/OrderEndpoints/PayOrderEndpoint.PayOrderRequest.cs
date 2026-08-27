using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total. Supply exactly one of <see cref="Card"/> (one-off card
/// payment) or <see cref="PaymentMethodId"/> (a saved card owned by the caller).
/// Card details are forwarded to PayPal and never stored or logged.
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public PayOrderCardRequest? Card { get; set; }

    [Range(1, int.MaxValue)]
    public int? PaymentMethodId { get; set; }
}

public class PayOrderCardRequest
{
    [Required]
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry in YYYY-MM format.</summary>
    [Required]
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public PayOrderAddressRequest? BillingAddress { get; set; }
}

public class PayOrderAddressRequest
{
    [Required]
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}
