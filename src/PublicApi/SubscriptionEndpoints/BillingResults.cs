using System.Security.Claims;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The single mapping from billing failures to HTTP results, shared by every subscription endpoint.</summary>
internal static class BillingResults
{
    /// <summary>The caller identity comes from the JWT (the token service emits the user name as the Name claim).</summary>
    public static string? UserName(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name);

    public static IResult From(BillingException ex) => ex.Failure switch
    {
        BillingFailure.PlanNotFound => Error(StatusCodes.Status404NotFound, ex.Message),
        BillingFailure.InProgress => Error(StatusCodes.Status409Conflict, ex.Message),
        BillingFailure.Rejected => Error(StatusCodes.Status422UnprocessableEntity, ex.Message),
        BillingFailure.OutcomeUnknown => Error(StatusCodes.Status503ServiceUnavailable,
            "The subscription could not be confirmed yet. Retry shortly; the request will not be duplicated."),
        _ => Error(StatusCodes.Status502BadGateway, "The billing provider is currently unavailable.")
    };

    private static IResult Error(int status, string message) =>
        Results.Text(new ErrorDetails { StatusCode = status, Message = message }.ToString(), "application/json", statusCode: status);
}
