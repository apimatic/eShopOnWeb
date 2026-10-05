using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing failures to caller-facing problem responses. A provider 4xx the caller can act on
/// (unknown plan, in-progress request) keeps its status; credential, quota, transport and provider
/// 5xx failures surface as 502 — the caller did nothing wrong and cannot fix them.
/// </summary>
public static class MaxioBillingResults
{
    public static IResult Problem(MaxioBillingException exception)
    {
        var statusCode = exception.ProviderStatusCode switch
        {
            null => StatusCodes.Status502BadGateway,
            var code when code is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => (int)code,
            _ => StatusCodes.Status502BadGateway
        };

        return Results.Problem(
            title: statusCode == StatusCodes.Status502BadGateway ? "Billing system unavailable." : "Subscription request failed.",
            detail: exception.Message,
            statusCode: statusCode);
    }
}