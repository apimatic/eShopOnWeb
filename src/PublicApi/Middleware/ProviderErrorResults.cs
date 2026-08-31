using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

/// <summary>
/// Maps a messaging-provider failure onto a caller-facing status, applied the same way
/// everywhere: our own credential/quota problems (401/403/429) are not the caller's fault
/// and surface as 5xx; other provider 4xx rejections are handed back at the same status
/// so the caller can act on them; transport and unknown failures are 502.
/// </summary>
public static class ProviderErrorResults
{
    public static IResult Map(MessagingException ex)
    {
        var status = (int?)ex.StatusCode;
        return status switch
        {
            401 or 403 => Results.Problem("The messaging provider rejected this application's credentials.", statusCode: 502),
            429 => Results.Problem("The messaging provider is temporarily limiting requests.", statusCode: 503),
            >= 400 and < 500 => Results.Problem(ex.Message, statusCode: status.Value),
            _ => Results.Problem("The messaging provider is unavailable.", statusCode: 502)
        };
    }
}
