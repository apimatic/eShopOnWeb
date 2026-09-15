using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Base for requests whose caller identity is taken from the JWT. <see cref="CallerId"/> is always
/// set server-side from the token before handling — never trusted from the request body.
/// </summary>
public abstract class AuthenticatedRequest : BaseRequest
{
    public string CallerId { get; set; } = string.Empty;
}

public static class CallerIdentity
{
    /// <summary>The caller's user name, as carried by the token's Name claim.</summary>
    public static string? Get(HttpContext httpContext) => httpContext.User?.Identity?.Name;
}

/// <summary>
/// Maps an <see cref="SmsNotificationException"/> to a caller-facing result, following one ladder
/// everywhere: our own credential/quota problems and transport failures become 5xx; a provider
/// rejection the caller could act on is passed through as its own 4xx. Never leaks the raw provider
/// message beyond the already-sanitised exception text.
/// </summary>
public static class ProviderErrorResults
{
    public static IResult From(SmsNotificationException exception)
    {
        var status = exception.StatusCode is null ? (int?)null : (int)exception.StatusCode.Value;

        return status switch
        {
            401 or 403 => Results.Problem("The SMS provider is unavailable.", statusCode: (int)HttpStatusCode.BadGateway),
            429 => Results.Problem("The SMS provider is temporarily unavailable.", statusCode: (int)HttpStatusCode.ServiceUnavailable),
            >= 400 and < 500 => Results.Problem(exception.Message, statusCode: status.Value),
            _ => Results.Problem(exception.Message, statusCode: (int)HttpStatusCode.BadGateway)
        };
    }
}
