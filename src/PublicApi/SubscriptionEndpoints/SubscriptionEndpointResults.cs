using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the subscription domain's typed failures onto HTTP results, so a caller gets an accurate
/// status code instead of a generic 500, and so no provider detail leaks that the caller cannot act on.
/// </summary>
public static class SubscriptionEndpointResults
{
    /// <summary>
    /// Resolves the caller's stable eShopOnWeb reference from the bearer token. The token carries it as
    /// the name claim; nothing supplied in a request body is ever trusted for identity.
    /// </summary>
    public static string? GetUserName(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static IResult FromException(Exception exception) => exception switch
    {
        SubscriptionNotFoundException notFound => Results.NotFound(new { error = notFound.Message }),

        // The request was understood but is not legal from the current state.
        InvalidSubscriptionTransitionException transition => Results.Conflict(new
        {
            error = transition.Message,
            currentState = transition.CurrentState.ToString(),
            legalActions = transition.LegalActions
        }),
        PlanChangeNotAllowedException planChange => Results.Conflict(new { error = planChange.Message }),
        SubscriptionNotBillableException notBillable => Results.Conflict(new
        {
            error = notBillable.Message,
            currentState = notBillable.State.ToString()
        }),

        // The preview the caller confirmed is no longer current; they must re-preview.
        StalePlanChangePreviewException stale => Results.Conflict(new { error = stale.Message, retryWith = "a fresh preview" }),

        // Configuration faults are the operator's problem, not the caller's.
        BillingConfigurationException configuration => Results.Problem(
            title: "Billing is misconfigured",
            detail: configuration.ProviderMessage,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        BillingUnavailableException unavailable => Results.Problem(
            title: "Billing is unavailable",
            detail: unavailable.ProviderMessage,
            statusCode: StatusCodes.Status503ServiceUnavailable),

        BillingProviderException provider => Results.BadRequest(new { error = provider.ProviderMessage }),
        ArgumentException argument => Results.BadRequest(new { error = argument.Message }),

        _ => throw exception
    };

    /// <summary>The failures an endpoint is expected to translate; anything else is a genuine fault.</summary>
    public static bool IsExpected(Exception exception) => exception is
        BillingProviderException or
        SubscriptionNotFoundException or
        SubscriptionNotBillableException or
        InvalidSubscriptionTransitionException or
        PlanChangeNotAllowedException or
        StalePlanChangePreviewException or
        ArgumentException;
}
