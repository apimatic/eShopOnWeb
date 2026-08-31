using System;
using System.Globalization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId)) throw new InvalidOperationException("PayPal:ClientId is required.");
        if (string.IsNullOrWhiteSpace(ClientSecret)) throw new InvalidOperationException("PayPal:ClientSecret is required.");
        if (!string.Equals(Environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PayPal:Environment must be Sandbox for this SDK release.");
        if (Currency.Length != 3 || Currency.ToUpperInvariant() != Currency || !IsAsciiLetters(Currency))
            throw new InvalidOperationException("PayPal:Currency must be a three-letter uppercase currency code.");
        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("PayPal:BaseUrl must be an absolute HTTPS URL when supplied.");
    }

    public string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static bool IsAsciiLetters(string value)
    {
        foreach (var c in value)
        {
            if (c is < 'A' or > 'Z') return false;
        }
        return true;
    }
}
