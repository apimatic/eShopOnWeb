using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

/// <summary>
/// The one place digital-file failures become HTTP responses.
/// </summary>
public static class DigitalFileErrors
{
    public static IResult Error(int statusCode, string message) =>
        Results.Json(new ErrorDetails { StatusCode = statusCode, Message = message }, statusCode: statusCode);

    /// <summary>
    /// Provider failures are never the caller's fault: the shop's own credentials, quota, or the provider's
    /// health are behind every one of them, so they map to 5xx.
    /// </summary>
    public static IResult FromProvider(DigitalFileProviderException ex) => ex.Failure switch
    {
        DigitalFileProviderFailure.RateLimited => Error(StatusCodes.Status503ServiceUnavailable, ex.Message),
        DigitalFileProviderFailure.Timeout => Error(StatusCodes.Status504GatewayTimeout, ex.Message),
        _ => Error(StatusCodes.Status502BadGateway, ex.Message),
    };
}
