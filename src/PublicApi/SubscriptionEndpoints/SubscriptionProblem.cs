using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The single place subscription failures become HTTP results.</summary>
public static class SubscriptionProblem
{
    public static IResult ToResult(SubscriptionException ex) => ex.Failure switch
    {
        SubscriptionFailure.InvalidUser => Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest),
        SubscriptionFailure.InvalidPlan => Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest),
        SubscriptionFailure.Rejected => Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        SubscriptionFailure.InProgress => Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Problem("Subscription billing is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
    };
}
