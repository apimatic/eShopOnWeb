using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription-billing failures to HTTP results: an unknown plan is a client
/// error (404); any other billing failure (misconfiguration or an upstream Maxio
/// error) is surfaced as a gateway error (502). Unknown exceptions are not handled
/// here and fall through to the global exception middleware.
/// </summary>
internal static class SubscriptionErrors
{
    public static bool IsHandled(Exception ex) =>
        ex is SubscriptionBillingException or MaxioApiException;

    public static IResult ToResult(Exception ex) => ex switch
    {
        PlanNotFoundException planNotFound => Results.Problem(
            title: "Subscription plan not found",
            detail: planNotFound.Message,
            statusCode: StatusCodes.Status404NotFound),

        SubscriptionBillingException billing => Results.Problem(
            title: "Subscription billing error",
            detail: billing.Message,
            statusCode: StatusCodes.Status502BadGateway),

        MaxioApiException maxio => Results.Problem(
            title: "Subscription billing error",
            detail: maxio.Message,
            statusCode: StatusCodes.Status502BadGateway),

        _ => throw ex,
    };
}
