using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates billing/identity exceptions into RFC 7807 problem responses so subscription
/// endpoints return meaningful, consistent status codes without leaking provider internals.
/// </summary>
internal static class SubscriptionProblemMapper
{
    public static IResult ToProblem(Exception exception) => exception switch
    {
        UnauthorizedAccessException ex => Results.Problem(
            title: "Unauthorized",
            detail: ex.Message,
            statusCode: StatusCodes.Status401Unauthorized),

        MaxioBillingException ex => Results.Problem(
            title: TitleFor(ex.Kind),
            detail: ex.CombinedErrors,
            statusCode: StatusFor(ex.Kind),
            extensions: ex.Errors.Count > 0 ? new Dictionary<string, object?> { ["errors"] = ex.Errors } : null),

        _ => Results.Problem(
            title: "Unexpected error",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError)
    };

    private static int StatusFor(MaxioBillingErrorKind kind) => kind switch
    {
        MaxioBillingErrorKind.NotFound => StatusCodes.Status404NotFound,
        MaxioBillingErrorKind.Validation => StatusCodes.Status400BadRequest,
        MaxioBillingErrorKind.Upstream => StatusCodes.Status502BadGateway,
        MaxioBillingErrorKind.Configuration => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(MaxioBillingErrorKind kind) => kind switch
    {
        MaxioBillingErrorKind.NotFound => "Plan not found",
        MaxioBillingErrorKind.Validation => "Invalid subscription request",
        MaxioBillingErrorKind.Upstream => "Billing service unavailable",
        MaxioBillingErrorKind.Configuration => "Billing not configured",
        _ => "Billing error"
    };
}
