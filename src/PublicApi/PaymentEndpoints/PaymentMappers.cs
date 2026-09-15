using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Maps between HTTP DTOs and the application/domain types, validating and normalising card input.</summary>
public static class PaymentMappers
{
    public static OrderPaymentDto ToDto(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Currency = payment.CurrencyCode,
        Amount = payment.Amount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        TotalRefunded = payment.TotalRefunded(),
        RefundableRemaining = payment.RefundableRemaining(),
        CreatedAt = payment.CreatedAt,
        UpdatedAt = payment.UpdatedAt,
        Refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                TotalRefundedAmount = r.TotalRefundedAmount,
                CreatedAt = r.CreatedAt
            })
            .ToList()
    };

    public static SavedCardDto ToDto(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.ExpiryYearMonth,
        CardholderName = method.CardHolderName,
        CreatedAt = method.CreatedAt
    };

    /// <summary>Validates and normalises a card DTO into gateway <see cref="CardDetails"/>.</summary>
    public static CardDetails ToCardDetails(CardDto? dto)
    {
        if (dto is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        var number = (dto.Number ?? string.Empty).Replace(" ", "").Replace("-", "");
        if (!Regex.IsMatch(number, "^[0-9]{13,19}$"))
        {
            throw new PaymentValidationException("Card number must be 13–19 digits.");
        }

        var expiry = NormalizeExpiry(dto.Expiry);

        var securityCode = (dto.SecurityCode ?? string.Empty).Trim();
        if (!Regex.IsMatch(securityCode, "^[0-9]{3,4}$"))
        {
            throw new PaymentValidationException("Card security code must be 3–4 digits.");
        }

        var countryCode = string.IsNullOrWhiteSpace(dto.BillingCountryCode) ? "US" : dto.BillingCountryCode!.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(countryCode, "^[A-Z]{2}$"))
        {
            throw new PaymentValidationException("Billing country code must be a 2-letter ISO country code.");
        }

        return new CardDetails
        {
            Number = number,
            ExpiryYearMonth = expiry,
            SecurityCode = securityCode,
            CardholderName = string.IsNullOrWhiteSpace(dto.CardholderName) ? null : dto.CardholderName!.Trim(),
            BillingCountryCode = countryCode,
            BillingAddressLine1 = NullIfBlank(dto.BillingAddressLine1),
            BillingAddressLine2 = NullIfBlank(dto.BillingAddressLine2),
            BillingAdminArea1 = NullIfBlank(dto.BillingState),
            BillingAdminArea2 = NullIfBlank(dto.BillingCity),
            BillingPostalCode = NullIfBlank(dto.BillingPostalCode)
        };
    }

    /// <summary>Accepts YYYY-MM, MM/YY or MM/YYYY and returns YYYY-MM.</summary>
    private static string NormalizeExpiry(string? expiry)
    {
        var value = (expiry ?? string.Empty).Trim();

        var isoMatch = Regex.Match(value, "^([0-9]{4})-(0[1-9]|1[0-2])$");
        if (isoMatch.Success)
        {
            return value;
        }

        var slashMatch = Regex.Match(value, "^(0[1-9]|1[0-2])/([0-9]{2}|[0-9]{4})$");
        if (slashMatch.Success)
        {
            var month = slashMatch.Groups[1].Value;
            var year = slashMatch.Groups[2].Value;
            if (year.Length == 2)
            {
                year = "20" + year;
            }
            return $"{year}-{month}";
        }

        throw new PaymentValidationException("Card expiry must be in the form YYYY-MM (or MM/YY).");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
