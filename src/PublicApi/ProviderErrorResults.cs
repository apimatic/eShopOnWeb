using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The one mapping from provider failure to caller-facing status, applied at
/// every endpoint that crosses the provider boundary. The provider's 4xx is the
/// caller's to act on — except 401/403 (our credentials) and 429 (our quota),
/// which are never the caller's fault.
/// </summary>
public static class ProviderErrorResults
{
    public static IResult Map(SmsProviderException ex) => (int?)ex.StatusCode switch
    {
        401 or 403 => Results.Problem("The messaging provider is unavailable.", statusCode: StatusCodes.Status502BadGateway),
        429 => Results.Problem("The messaging provider is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable),
        >= 400 and < 500 => Results.Problem(ex.Message, statusCode: (int)ex.StatusCode!.Value),
        _ => Results.Problem("The messaging provider is unavailable.", statusCode: StatusCodes.Status502BadGateway)
    };
}
