using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>Validates a <see cref="CardDto"/> and maps it to the SDK-free gateway input.</summary>
public static class CardMapping
{
    public static PayPalCardInput ToInput(CardDto card)
    {
        if (card is null)
            throw new PaymentValidationException("Card details are required.");

        var number = (card.CardNumber ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (string.IsNullOrWhiteSpace(number))
            throw new PaymentValidationException("A card number is required.");

        if (card.ExpiryMonth < 1 || card.ExpiryMonth > 12)
            throw new PaymentValidationException("Card expiry month must be between 1 and 12.");

        var year = card.ExpiryYear;
        if (year < 100) year += 2000; // accept 2-digit years
        if (year < 2000 || year > 2100)
            throw new PaymentValidationException("Card expiry year is not valid.");

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
            throw new PaymentValidationException("A card security code is required.");

        // PayPal wire format for expiry is YYYY-MM.
        var expiry = $"{year:D4}-{card.ExpiryMonth:D2}";
        var country = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode!.Trim();

        return new PayPalCardInput(
            Number: number,
            ExpiryYearMonth: expiry,
            SecurityCode: card.SecurityCode.Trim(),
            CardholderName: string.IsNullOrWhiteSpace(card.CardholderName) ? null : card.CardholderName!.Trim(),
            BillingLine1: string.IsNullOrWhiteSpace(card.BillingLine1) ? null : card.BillingLine1!.Trim(),
            BillingCity: string.IsNullOrWhiteSpace(card.BillingCity) ? null : card.BillingCity!.Trim(),
            BillingState: string.IsNullOrWhiteSpace(card.BillingState) ? null : card.BillingState!.Trim(),
            BillingPostalCode: string.IsNullOrWhiteSpace(card.BillingPostalCode) ? null : card.BillingPostalCode!.Trim(),
            BillingCountryCode: country);
    }
}
