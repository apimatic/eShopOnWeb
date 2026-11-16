using System.Text.Json.Serialization;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardAddressRequest? Address { get; set; }

    // Common aliases accepted in addition to the canonical field names.
    public int? ExpMonth { get; set; }
    public int? ExpYear { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? Cvc { get; set; }
    public CardRequest? Card { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public int ResolveExpirationMonth() =>
        ExpirationMonth != 0 ? ExpirationMonth : (ExpMonth ?? ExpiryMonth ?? 0);

    public int ResolveExpirationYear() =>
        ExpirationYear != 0 ? ExpirationYear : (ExpYear ?? ExpiryYear ?? 0);

    public string ResolveSecurityCode() =>
        !string.IsNullOrWhiteSpace(SecurityCode) ? SecurityCode : (Cvc ?? string.Empty);
}
