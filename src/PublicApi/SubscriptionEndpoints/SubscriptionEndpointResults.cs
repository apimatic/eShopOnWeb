using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps billing failures to appropriate HTTP problem responses.</summary>
internal static class SubscriptionEndpointResults
{
    public static IResult FromBillingException(MaxioBillingException exception)
    {
        // A bad/unknown plan handle is a client error.
        if (exception is PlanNotFoundException)
            return Results.Problem(
                title: "Unknown subscription plan",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);

        // Everything else is an upstream billing-provider failure.
        return Results.Problem(
            title: "Billing provider error",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
}
