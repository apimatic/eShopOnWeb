using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The one place a messaging-provider failure becomes a caller-facing status.
/// Provider 401/403 (our credentials) and 429 (our quota) are not the caller's fault,
/// so they surface as 5xx; other provider 4xx are handed back so the caller can act.
/// </summary>
public static class ProviderErrorResults
{
    public static IResult Map(SmsProviderException ex) => (int?)ex.StatusCode switch
    {
        401 or 403 => Results.Problem("The messaging provider rejected the application's credentials.", statusCode: 502),
        429 => Results.Problem("The messaging provider is temporarily unavailable.", statusCode: 503),
        >= 400 and < 500 => Results.Problem(ex.Message, statusCode: (int)ex.StatusCode!),
        _ => Results.Problem("The messaging provider is unavailable.", statusCode: 502),
    };
}
