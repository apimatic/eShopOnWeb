using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Walmart;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Maps a <see cref="WalmartIntegrationException"/> to a caller-facing HTTP result, with one
/// consistent status mapping across every Walmart endpoint. Our credentials/quota faults become
/// 5xx (the caller cannot fix them); the provider's own 4xx on the caller's input is passed
/// through; everything else is 502.
/// </summary>
internal static class WalmartResults
{
    public static IResult Problem(WalmartIntegrationException ex)
    {
        var status = ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => StatusCodes.Status502BadGateway,
            HttpStatusCode.TooManyRequests => StatusCodes.Status503ServiceUnavailable,
            { } s when (int)s >= 400 && (int)s < 500 => (int)s,
            _ => StatusCodes.Status502BadGateway,
        };

        return Results.Problem(detail: ex.Message, statusCode: status, title: $"Walmart {ex.Operation} failed");
    }
}
