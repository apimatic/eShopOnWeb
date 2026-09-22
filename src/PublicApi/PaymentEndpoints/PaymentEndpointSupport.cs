using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: identity, cancellation, error mapping, DTO mapping.</summary>
public static class PaymentEndpointSupport
{
    /// <summary>The caller's user name from the JWT (ClaimTypes.Name). Throws if unauthenticated.</summary>
    public static string RequireUserName(IHttpContextAccessor accessor)
    {
        var name = accessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new PaymentFlowException(PaymentFlowError.Forbidden, "The caller identity could not be determined.");
        return name!;
    }

    public static CancellationToken RequestAborted(IHttpContextAccessor accessor) =>
        accessor.HttpContext?.RequestAborted ?? CancellationToken.None;

    /// <summary>Runs an endpoint body, translating payment/gateway failures to the right HTTP status.</summary>
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PaymentFlowException ex)
        {
            var status = ex.Error switch
            {
                PaymentFlowError.NotFound => StatusCodes.Status404NotFound,
                PaymentFlowError.Forbidden => StatusCodes.Status403Forbidden,
                PaymentFlowError.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { error = ex.Message }, statusCode: status);
        }
        catch (PayPalGatewayException ex)
        {
            var status = ex.Kind switch
            {
                PaymentGatewayErrorKind.Validation => StatusCodes.Status400BadRequest,
                PaymentGatewayErrorKind.ApprovalRequired => StatusCodes.Status400BadRequest,
                PaymentGatewayErrorKind.NotFound => StatusCodes.Status404NotFound,
                PaymentGatewayErrorKind.Conflict => StatusCodes.Status409Conflict,
                PaymentGatewayErrorKind.AuthorizationNotRenewable => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status502BadGateway // Unavailable / Unknown — not the caller's fault
            };
            // debug_id is safe to surface — it is a correlation id, not a secret — and helps operators trace.
            return Results.Json(new { error = ex.Message, issue = ex.Issue, debugId = ex.DebugId }, statusCode: status);
        }
    }

    public static CardDetails? ToCardDetails(CardDto? dto)
    {
        if (dto is null) return null;
        if (string.IsNullOrWhiteSpace(dto.Number) || string.IsNullOrWhiteSpace(dto.Expiry))
            return null;
        CardBillingAddress? billing = dto.BillingAddress is null
            ? null
            : new CardBillingAddress(dto.BillingAddress.AddressLine1, dto.BillingAddress.AdminArea2,
                dto.BillingAddress.AdminArea1, dto.BillingAddress.PostalCode, dto.BillingAddress.CountryCode);
        return new CardDetails(dto.Number!.Replace(" ", string.Empty), dto.Expiry!.Trim(),
            dto.SecurityCode, dto.CardholderName, billing);
    }
}

/// <summary>Card details supplied by the caller for a one-off payment or to save a card. Never stored/logged raw.</summary>
public class CardDto
{
    /// <summary>Primary account number.</summary>
    public string? Number { get; set; }

    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form (e.g. <c>2030-01</c>).</summary>
    public string? Expiry { get; set; }

    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? AddressLine1 { get; set; }
    public string? AdminArea2 { get; set; } // city
    public string? AdminArea1 { get; set; } // state / province
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; } // ISO 3166-1 alpha-2
}
