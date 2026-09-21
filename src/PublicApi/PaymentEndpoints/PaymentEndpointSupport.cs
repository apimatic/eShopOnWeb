using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Shared helpers for the payment endpoints: caller identity, a call budget, DTO mapping and error mapping.</summary>
internal static class PaymentEndpointSupport
{
    /// <summary>The whole-call budget for a single API request (bounds any PayPal round-trips it makes).</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(90);

    public static string GetBuyerId(HttpContext http)
    {
        var name = http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(PaymentError.Forbidden, "The caller identity could not be determined.");
        }
        return name;
    }

    /// <summary>A cancellation token bounding the whole request, linked to the client disconnecting.</summary>
    public static CancellationTokenSource CreateBudget(HttpContext http)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    public static IReadOnlyList<OrderLineInput> ToLineInputs(IEnumerable<OrderLineDto> items) =>
        items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToList();

    public static ShippingAddressInput? ToShippingInput(ShippingAddressDto? dto) =>
        dto is null ? null : new ShippingAddressInput(dto.Street, dto.City, dto.State, dto.Country, dto.ZipCode);

    public static CardInput? ToCardInput(CardDto? card)
    {
        if (card is null)
        {
            return null;
        }
        BillingAddressInput? billing = card.BillingAddress is null
            ? null
            : new BillingAddressInput(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode);
        return new CardInput(card.Number, card.Expiry, card.SecurityCode, card.CardholderName, billing);
    }

    /// <summary>Map an expected payment failure to a coherent HTTP result. Rethrows anything unexpected.</summary>
    public static IResult ToResult(Exception ex)
    {
        switch (ex)
        {
            case PaymentOperationException op:
                var opStatus = op.Error switch
                {
                    PaymentError.NotFound => StatusCodes.Status404NotFound,
                    PaymentError.Forbidden => StatusCodes.Status403Forbidden,
                    PaymentError.Conflict => StatusCodes.Status409Conflict,
                    PaymentError.ChallengeRequired => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };
                return Results.Json(new { statusCode = opStatus, message = op.Message }, statusCode: opStatus);

            case PayPalGatewayException gw:
                var (gwStatus, gwMessage) = gw.Kind switch
                {
                    PayPalFailureKind.InvalidRequest => (StatusCodes.Status400BadRequest, gw.Message),
                    PayPalFailureKind.NotFound => (StatusCodes.Status404NotFound, gw.Message),
                    PayPalFailureKind.Conflict => (StatusCodes.Status409Conflict, gw.Message),
                    PayPalFailureKind.AuthorizationExpired => (StatusCodes.Status409Conflict, gw.Message),
                    PayPalFailureKind.AuthorizationNotRenewable => (StatusCodes.Status409Conflict, gw.Message),
                    PayPalFailureKind.Unknown => (StatusCodes.Status502BadGateway, "Payment provider outcome is unknown; please reconcile before retrying."),
                    _ => (StatusCodes.Status502BadGateway, "Payment provider is temporarily unavailable.")
                };
                return Results.Json(
                    new { statusCode = gwStatus, message = gwMessage, issue = gw.IssueCode, debugId = gw.DebugId },
                    statusCode: gwStatus);

            default:
                // Not an expected payment failure: let the global exception middleware handle it.
                throw ex;
        }
    }
}
