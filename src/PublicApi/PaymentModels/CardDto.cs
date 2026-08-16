using System;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentModels;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to be vaulted.
/// These values are never persisted to the application database and never logged.
/// </summary>
public class CardDto
{
    public string CardNumber { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;

    public string BillingAddressLine1 { get; set; } = string.Empty;
    public string? BillingAddressLine2 { get; set; }
    public string BillingCity { get; set; } = string.Empty;
    public string? BillingState { get; set; }
    public string BillingPostalCode { get; set; } = string.Empty;
    public string BillingCountryCode { get; set; } = string.Empty;

    /// <summary>Validate and convert to the domain-neutral card details.</summary>
    public CardPaymentDetails ToDomain()
    {
        if (string.IsNullOrWhiteSpace(CardNumber))
        {
            throw new ArgumentException("Card number is required.");
        }
        if (ExpiryMonth is < 1 or > 12)
        {
            throw new ArgumentException("Card expiry month must be between 1 and 12.");
        }
        if (ExpiryYear < 2000)
        {
            throw new ArgumentException("Card expiry year must be a four-digit year.");
        }
        if (string.IsNullOrWhiteSpace(SecurityCode))
        {
            throw new ArgumentException("Card security code is required.");
        }
        if (string.IsNullOrWhiteSpace(CardholderName))
        {
            throw new ArgumentException("Cardholder name is required.");
        }
        if (string.IsNullOrWhiteSpace(BillingAddressLine1) ||
            string.IsNullOrWhiteSpace(BillingCity) ||
            string.IsNullOrWhiteSpace(BillingPostalCode) ||
            string.IsNullOrWhiteSpace(BillingCountryCode))
        {
            throw new ArgumentException("A complete billing address (line 1, city, postal code, country code) is required.");
        }

        return new CardPaymentDetails(
            Number: CardNumber.Replace(" ", string.Empty).Trim(),
            ExpiryMonth: ExpiryMonth,
            ExpiryYear: ExpiryYear,
            SecurityCode: SecurityCode.Trim(),
            CardholderName: CardholderName.Trim(),
            BillingAddressLine1: BillingAddressLine1.Trim(),
            BillingAddressLine2: string.IsNullOrWhiteSpace(BillingAddressLine2) ? null : BillingAddressLine2.Trim(),
            BillingCity: BillingCity.Trim(),
            BillingState: string.IsNullOrWhiteSpace(BillingState) ? null : BillingState.Trim(),
            BillingPostalCode: BillingPostalCode.Trim(),
            BillingCountryCode: BillingCountryCode.Trim());
    }
}
