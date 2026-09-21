using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ---- Shared request input DTOs (bound from the JSON body) ----

/// <summary>A card for a one-off payment or to save. The card number is never stored or logged.</summary>
public class CardInput
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in ISO-8601 YYYY-MM (e.g. "2030-12").</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }
}

public class BillingAddressInput
{
    /// <summary>2-character ISO 3166-1 country code (required by PayPal).</summary>
    public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? PostalCode { get; set; }
}

/// <summary>
/// Cross-cutting helpers for the payment endpoints: reading the caller identity from the JWT and
/// translating domain/gateway exceptions into caller-safe HTTP responses.
/// </summary>
public static class PaymentApiSupport
{
    /// <summary>The eShop BuyerId is the caller's identity name (username/email) from the token.</summary>
    public static string RequireBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name)
                      ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            throw new PaymentValidationException("The token does not identify a shopper.");
        return buyerId;
    }

    public static CardDetails ToCardDetails(CardInput card) => new(
        Number: card.Number,
        ExpiryYearMonth: card.Expiry,
        SecurityCode: card.SecurityCode,
        CardholderName: card.CardholderName,
        BillingAddress: card.BillingAddress is null ? null : new CardBillingAddress(
            CountryCode: card.BillingAddress.CountryCode,
            AddressLine1: card.BillingAddress.AddressLine1,
            AddressLine2: card.BillingAddress.AddressLine2,
            AdminArea1: card.BillingAddress.AdminArea1,
            AdminArea2: card.BillingAddress.AdminArea2,
            PostalCode: card.BillingAddress.PostalCode));

    /// <summary>Runs an endpoint body and maps known failures to appropriate status codes.</summary>
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PaymentValidationException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (PaymentNotFoundException ex)
        {
            return Problem(StatusCodes.Status404NotFound, ex.Message);
        }
        catch (PaymentConflictException ex)
        {
            return Problem(StatusCodes.Status409Conflict, ex.Message);
        }
        catch (PayerActionRequiredException ex)
        {
            // A browser-approval challenge (e.g. 3-D Secure). We deliberately do not build a
            // round-trip; surface it clearly so an operator can act.
            return Problem(StatusCodes.Status402PaymentRequired, ex.Message);
        }
        catch (PaymentGatewayException ex)
        {
            return Problem(MapGatewayStatus(ex), ex.Message);
        }
    }

    private static int MapGatewayStatus(PaymentGatewayException ex)
    {
        var name = ex.ProviderName?.ToUpperInvariant();
        // The provider rejected the caller's input — they can fix it.
        if (name is "VALIDATION_ERROR" or "INVALID_REQUEST" or "UNPROCESSABLE_ENTITY")
            return StatusCodes.Status422UnprocessableEntity;
        if (name is "RESOURCE_NOT_FOUND")
            return StatusCodes.Status404NotFound;
        if (ex.StatusCode is 400 or 422)
            return StatusCodes.Status422UnprocessableEntity;
        if (ex.StatusCode is 404)
            return StatusCodes.Status404NotFound;
        // Our credentials/quota (401/403/429), transport, or provider 5xx — not the caller's fault.
        return StatusCodes.Status502BadGateway;
    }

    private static IResult Problem(int status, string message) =>
        Results.Json(new ErrorResponse(status, message), statusCode: status);

    public record ErrorResponse(int StatusCode, string Message);
}
