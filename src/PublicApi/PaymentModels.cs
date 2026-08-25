using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Card details for a one-off payment or a save-card request. Never persisted or logged.</summary>
public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = string.Empty;
}

public static class PaymentRequestMapper
{
    public static CardDetails ToCardDetails(this CardPaymentRequest request)
    {
        var expiry = $"{request.ExpiryYear:D4}-{request.ExpiryMonth:D2}";
        BillingAddress? billingAddress = request.BillingAddress is null
            ? null
            : new BillingAddress(
                request.BillingAddress.AddressLine1,
                request.BillingAddress.AddressLine2,
                request.BillingAddress.AdminArea1,
                request.BillingAddress.AdminArea2,
                request.BillingAddress.PostalCode,
                request.BillingAddress.CountryCode);

        return new CardDetails(request.Number, expiry, request.SecurityCode, request.CardholderName, billingAddress);
    }
}
