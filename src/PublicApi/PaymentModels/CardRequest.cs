using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by a caller to pay a one-off order or to save a card. This is a transient
/// input only — the application never stores or logs these values; they are forwarded to PayPal over TLS.
/// </summary>
public class CardRequest
{
    /// <summary>Primary account number, spaces allowed (e.g. "4111 1111 1111 1111").</summary>
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }

    /// <summary>Validates and maps to the core <see cref="CardDetails"/> carrier.</summary>
    public CardDetails ToCardDetails()
    {
        if (string.IsNullOrWhiteSpace(CardNumber))
        {
            throw new InvalidPaymentRequestException("A card number is required.");
        }

        if (ExpiryMonth is < 1 or > 12)
        {
            throw new InvalidPaymentRequestException("Card expiry month must be between 1 and 12.");
        }

        if (ExpiryYear is < 2000 or > 2100)
        {
            throw new InvalidPaymentRequestException("Card expiry year must be a four-digit year.");
        }

        if (string.IsNullOrWhiteSpace(SecurityCode))
        {
            throw new InvalidPaymentRequestException("A card security code (CVC) is required.");
        }

        if (string.IsNullOrWhiteSpace(CardholderName))
        {
            throw new InvalidPaymentRequestException("A cardholder name is required.");
        }

        if (BillingAddress is null)
        {
            throw new InvalidPaymentRequestException("A billing address is required.");
        }

        var expiry = $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
        var normalisedNumber = CardNumber.Replace(" ", string.Empty).Trim();

        return new CardDetails(
            normalisedNumber,
            expiry,
            SecurityCode.Trim(),
            CardholderName.Trim(),
            BillingAddress.ToCardBillingAddress());
    }
}

/// <summary>Billing address for a card.</summary>
public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>2-letter ISO country code, e.g. "US".</summary>
    public string CountryCode { get; set; } = string.Empty;

    public CardBillingAddress ToCardBillingAddress()
    {
        if (string.IsNullOrWhiteSpace(AddressLine1))
        {
            throw new InvalidPaymentRequestException("Billing address line 1 is required.");
        }

        if (string.IsNullOrWhiteSpace(City))
        {
            throw new InvalidPaymentRequestException("Billing address city is required.");
        }

        if (string.IsNullOrWhiteSpace(PostalCode))
        {
            throw new InvalidPaymentRequestException("Billing address postal code is required.");
        }

        if (string.IsNullOrWhiteSpace(CountryCode))
        {
            throw new InvalidPaymentRequestException("Billing address country code is required.");
        }

        return new CardBillingAddress(
            AddressLine1.Trim(),
            string.IsNullOrWhiteSpace(AddressLine2) ? null : AddressLine2.Trim(),
            City.Trim(),
            string.IsNullOrWhiteSpace(State) ? null : State!.Trim(),
            PostalCode.Trim(),
            CountryCode.Trim());
    }
}
