using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.WalmartStockEndpoints;

/// <summary>
/// Maps a <see cref="WalmartInventoryException"/> to a caller-facing HTTP result. A problem the
/// caller caused (a Walmart 4xx) is passed back as that status; our own credential/quota problems
/// and anything the marketplace could not answer become a 5xx — never a message that leaks the SDK.
/// </summary>
internal static class WalmartResults
{
    public static IResult ToResult(WalmartInventoryException ex)
    {
        int? status = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null;
        return status switch
        {
            401 or 403 => Results.Problem("Walmart Marketplace is unavailable.", statusCode: StatusCodes.Status502BadGateway),
            429 => Results.Problem("Walmart Marketplace is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable),
            >= 400 and < 500 => Results.Problem(ex.Message, statusCode: status.Value),
            _ => Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway),
        };
    }
}
