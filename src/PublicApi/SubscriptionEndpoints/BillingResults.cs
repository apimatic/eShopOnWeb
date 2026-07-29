using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps a <see cref="BillingException"/> to an HTTP problem response: caller-fixable failures become
/// <c>400 Bad Request</c>, while upstream/transient billing-system failures become
/// <c>502 Bad Gateway</c> (the billing system of record is an upstream dependency).
/// </summary>
internal static class BillingResults
{
    public static IResult Problem(BillingException exception)
    {
        var statusCode = exception.IsClientError
            ? (int)HttpStatusCode.BadRequest
            : (int)HttpStatusCode.BadGateway;

        var title = exception.IsClientError ? "Invalid subscription request" : "Billing system unavailable";

        return Results.Problem(detail: exception.Message, statusCode: statusCode, title: title);
    }
}
