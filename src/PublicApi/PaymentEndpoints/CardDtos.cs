using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Raw card details supplied by a caller. Never persisted or logged by this app.</summary>
public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Card expiry as YYYY-MM (PayPal format).</summary>
    public string Expiry { get; set; } = string.Empty;

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }

    /// <summary>State / province.</summary>
    public string? State { get; set; }

    /// <summary>City / town.</summary>
    public string? City { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>Two-letter country code, e.g. "US".</summary>
    public string? CountryCode { get; set; }
}

internal static class CardMapping
{
    public static CardDetails ToCardDetails(this CardDetailsRequest request) => new(
        request.Number,
        request.Expiry,
        request.SecurityCode,
        request.CardholderName,
        request.BillingAddress is null
            ? null
            : new CardBillingAddress(
                request.BillingAddress.AddressLine1,
                request.BillingAddress.AddressLine2,
                request.BillingAddress.State,
                request.BillingAddress.City,
                request.BillingAddress.PostalCode,
                request.BillingAddress.CountryCode));
}

internal static class HttpContextExtensions
{
    /// <summary>The authenticated caller's identity (email/username), used as the buyer id.</summary>
    public static string BuyerId(this HttpContext http) =>
        http.User.Identity?.Name
        ?? throw new System.InvalidOperationException("Authenticated caller has no name claim.");

    public static int RouteInt(this HttpContext http, string key) =>
        int.Parse((string)http.Request.RouteValues[key]!, CultureInfo.InvariantCulture);
}
