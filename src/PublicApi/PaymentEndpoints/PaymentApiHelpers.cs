using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Card details as accepted over the API. Never persisted or logged by the app.</summary>
public class CardRequest
{
    public string Number { get; set; } = string.Empty;

    /// <summary>Expiry as "YYYY-MM" (also accepts "MM/YY" or "MM/YYYY").</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public static class PaymentApiHelpers
{
    /// <summary>The signed-in shopper's id (their username), taken from the JWT.</summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? user.FindFirstValue("name");

    public static CardDetails ToCardDetails(this CardRequest card) => new(
        Number: (card.Number ?? string.Empty).Replace(" ", string.Empty).Trim(),
        Expiry: NormalizeExpiry(card.Expiry),
        SecurityCode: card.SecurityCode,
        CardholderName: card.CardholderName,
        BillingAddress: card.BillingAddress is null
            ? null
            : new BillingAddress(card.BillingAddress.Line1, card.BillingAddress.Line2,
                card.BillingAddress.City, card.BillingAddress.State,
                card.BillingAddress.PostalCode, card.BillingAddress.CountryCode));

    /// <summary>Normalizes a card expiry to PayPal's "YYYY-MM" form.</summary>
    public static string NormalizeExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) return string.Empty;
        expiry = expiry.Trim();

        // Already YYYY-MM
        if (Regex.IsMatch(expiry, @"^\d{4}-\d{2}$")) return expiry;

        // MM/YY or MM/YYYY or MM-YY etc.
        var m = Regex.Match(expiry, @"^(\d{1,2})[/\-](\d{2}|\d{4})$");
        if (m.Success)
        {
            var month = int.Parse(m.Groups[1].Value).ToString("D2");
            var year = m.Groups[2].Value;
            if (year.Length == 2) year = "20" + year;
            return $"{year}-{month}";
        }

        return expiry; // let PayPal validate anything unexpected
    }

    /// <summary>Maps a failed result's status to the right HTTP response.</summary>
    private static Microsoft.AspNetCore.Http.IResult MapStatus(ResultStatus status, IEnumerable<string> errors,
        IEnumerable<ValidationError> validationErrors)
    {
        return status switch
        {
            ResultStatus.NotFound => Results.NotFound(new { errors = errors.DefaultIfEmpty("Not found.") }),
            ResultStatus.Invalid => Results.BadRequest(new { errors = validationErrors.Select(v => v.ErrorMessage) }),
            ResultStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            ResultStatus.Unauthorized => Results.StatusCode(StatusCodes.Status401Unauthorized),
            ResultStatus.Error => Results.UnprocessableEntity(new { errors = errors.DefaultIfEmpty("The request could not be processed.") }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    public static Microsoft.AspNetCore.Http.IResult ToProblem<T>(this Result<T> result) =>
        MapStatus(result.Status, result.Errors, result.ValidationErrors);

    public static Microsoft.AspNetCore.Http.IResult ToProblem(this Result result) =>
        MapStatus(result.Status, result.Errors, result.ValidationErrors);
}
