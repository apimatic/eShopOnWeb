using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionEndpointErrors
{
    public static IResult Upstream(MaxioApiException ex)
    {
        return Results.Problem(
            detail: string.Join(" ", ex.Errors),
            title: "Maxio Advanced Billing returned an error response.",
            statusCode: (int)HttpStatusCode.BadGateway,
            extensions: new Dictionary<string, object?> { { "maxioStatus", ex.StatusCode } });
    }

    public static IResult NotConfigured(MaxioConfigurationException ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Maxio billing is not configured.",
            statusCode: (int)HttpStatusCode.ServiceUnavailable);
    }

    public static IResult PlanNotFound(SubscriptionPlanNotFoundException ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "Unknown subscription plan.",
            statusCode: (int)HttpStatusCode.NotFound);
    }
}
