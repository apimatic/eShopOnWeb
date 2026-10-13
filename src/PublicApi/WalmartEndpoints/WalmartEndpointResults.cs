using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

internal static class WalmartEndpointResults
{
    /// <summary>
    /// Maps a Walmart integration failure to a caller-facing result, using the caller-safe status
    /// and message the boundary already decided. Never exposes the secret or raw SDK detail.
    /// </summary>
    public static IResult Error(WalmartIntegrationException ex) =>
        Results.Json(new { message = ex.Message }, statusCode: (int)ex.StatusCode);
}
