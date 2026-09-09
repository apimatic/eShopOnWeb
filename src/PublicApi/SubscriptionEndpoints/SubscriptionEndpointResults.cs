using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared result helpers so all subscription endpoints surface billing errors consistently.
/// </summary>
internal static class SubscriptionEndpointResults
{
    /// <summary>
    /// Maps a failure talking to Maxio to a 502 Bad Gateway — the eShopOnWeb API itself is healthy,
    /// but its upstream billing dependency returned an error.
    /// </summary>
    public static IResult UpstreamError(MaxioApiException exception) => Results.Problem(
        title: "Billing service error",
        detail: exception.Message,
        statusCode: StatusCodes.Status502BadGateway);
}
