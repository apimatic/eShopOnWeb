using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Translates a <see cref="BillingException"/> (an upstream failure in the billing system) into
/// an appropriate HTTP problem response.
/// </summary>
internal static class BillingProblem
{
    public static IResult From(BillingException exception)
    {
        var status = exception.StatusCode == StatusCodes.Status409Conflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status502BadGateway;

        return Results.Problem(
            title: "Billing system error",
            detail: exception.Message,
            statusCode: status);
    }
}
