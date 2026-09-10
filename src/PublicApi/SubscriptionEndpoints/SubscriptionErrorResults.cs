using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates billing failures into consistent HTTP problem responses at the API boundary.
/// </summary>
internal static class SubscriptionErrorResults
{
    public static IResult FromBillingException(BillingException exception) => exception switch
    {
        PlanNotFoundException planNotFound => Results.Problem(
            title: "Plan not found",
            detail: planNotFound.Message,
            statusCode: (int)HttpStatusCode.NotFound),

        BillingUpstreamException => Results.Problem(
            title: "Billing provider unavailable",
            detail: exception.Message,
            statusCode: (int)HttpStatusCode.BadGateway),

        _ => Results.Problem(
            title: "Billing error",
            detail: exception.Message,
            statusCode: (int)HttpStatusCode.InternalServerError)
    };

    public static IResult MissingIdentity() => Results.Problem(
        title: "Unauthorized",
        detail: "The access token does not identify a user.",
        statusCode: (int)HttpStatusCode.Unauthorized);
}
