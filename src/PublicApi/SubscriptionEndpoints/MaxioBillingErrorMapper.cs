using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing failures onto HTTP responses (RFC 7807 problem details).
/// </summary>
public static class MaxioBillingErrorMapper
{
    private const int MaxUpstreamDetailLength = 500;

    public static IResult ToProblemResult(this MaxioBillingException ex) =>
        Results.Problem(ToProblemDetails(ex));

    public static ActionResult ToActionResult(this MaxioBillingException ex)
    {
        var problem = ToProblemDetails(ex);
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    public static ProblemDetails ToProblemDetails(this MaxioBillingException ex)
    {
        switch (ex)
        {
            case PlanNotFoundException planNotFound:
                return new ProblemDetails
                {
                    Title = "Plan not found",
                    Detail = planNotFound.Message,
                    Status = (int)HttpStatusCode.NotFound
                };
            case MaxioApiException api when api.UpstreamStatusCode is (int)HttpStatusCode.BadRequest
                                                     or (int)HttpStatusCode.NotFound
                                                     or (int)HttpStatusCode.UnprocessableEntity:
                return new ProblemDetails
                {
                    Title = "Billing request rejected",
                    Detail = api.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Extensions = { ["upstreamErrors"] = Truncate(api.UpstreamErrors) }
                };
            case MaxioApiException api:
                return new ProblemDetails
                {
                    Title = "Billing system unavailable",
                    Detail = api.Message,
                    Status = (int)HttpStatusCode.BadGateway,
                    Extensions = { ["upstreamErrors"] = Truncate(api.UpstreamErrors) }
                };
            default:
                return new ProblemDetails
                {
                    Title = "Subscription billing failure",
                    Detail = ex.Message,
                    Status = (int)HttpStatusCode.InternalServerError
                };
        }
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= MaxUpstreamDetailLength ? value : value[..MaxUpstreamDetailLength] + "…";
    }
}
