using System.Linq;
using System.Security.Claims;
using System.Threading;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

/// <summary>
/// Small helpers shared by the notification-feature endpoints: resolving the caller's identity from the
/// token, reaching per-request scoped services, and turning an <see cref="Result"/> into an HTTP result.
/// </summary>
public static class EndpointHelpers
{
    /// <summary>The caller's identity (username / email) as carried by the JWT, or null if unauthenticated.</summary>
    public static string? GetOwnerId(this IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        return user?.FindFirstValue(ClaimTypes.Name) ?? user?.Identity?.Name;
    }

    public static CancellationToken RequestAborted(this IHttpContextAccessor accessor) =>
        accessor.HttpContext?.RequestAborted ?? CancellationToken.None;

    /// <summary>Reads a request header value, or null if absent.</summary>
    public static string? Header(this IHttpContextAccessor accessor, string name)
    {
        var headers = accessor.HttpContext?.Request.Headers;
        if (headers is not null && headers.TryGetValue(name, out var value))
        {
            return value.ToString();
        }
        return null;
    }

    /// <summary>Resolves a scoped service from the current request scope.</summary>
    public static T RequestService<T>(this IHttpContextAccessor accessor) where T : notnull =>
        accessor.HttpContext!.RequestServices.GetRequiredService<T>();

    /// <summary>Maps a non-success <see cref="Result{T}"/> to an HTTP result.</summary>
    public static IResult ToStatusResult<T>(this Result<T> result) => result.Status switch
    {
        ResultStatus.NotFound => Results.NotFound(),
        ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
        ResultStatus.Forbidden => Results.Forbid(),
        ResultStatus.Unauthorized => Results.Unauthorized(),
        _ => Results.Problem(string.Join("; ", result.Errors), statusCode: StatusCodes.Status502BadGateway)
    };

    /// <summary>Maps a non-generic <see cref="Result"/> to an HTTP result.</summary>
    public static IResult ToHttpResult(this Result result) => result.Status switch
    {
        ResultStatus.Ok => Results.Ok(),
        ResultStatus.NotFound => Results.NotFound(),
        ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
        ResultStatus.Forbidden => Results.Forbid(),
        ResultStatus.Unauthorized => Results.Unauthorized(),
        _ => Results.Problem(string.Join("; ", result.Errors), statusCode: StatusCodes.Status502BadGateway)
    };
}
