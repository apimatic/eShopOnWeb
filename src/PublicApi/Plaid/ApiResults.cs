using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Shared helpers: error answers with a status and a reason, and caller identity.</summary>
public static class ApiResults
{
    public static IResult Error(int statusCode, string reason) =>
        Results.Json(new { statusCode, message = reason }, statusCode: statusCode);

    public static IResult BadRequest(string reason) => Error(StatusCodes.Status400BadRequest, reason);
    public static IResult NotFound(string reason) => Error(StatusCodes.Status404NotFound, reason);
    public static IResult Conflict(string reason) => Error(StatusCodes.Status409Conflict, reason);

    /// <summary>Maps a Plaid failure to the status the caller should see.</summary>
    public static IResult FromPlaid(PlaidException ex)
    {
        var reason = !string.IsNullOrWhiteSpace(ex.DisplayMessage) ? ex.DisplayMessage! : ex.Message;
        if (ex.IsNotConfigured) return Error(StatusCodes.Status503ServiceUnavailable, reason);
        if (ex.IsUnavailable) return Error(StatusCodes.Status502BadGateway, $"The bank service is unavailable: {reason}");
        if (ex.HttpStatus == 429 || ex.ErrorType == "RATE_LIMIT_EXCEEDED") return Error(StatusCodes.Status429TooManyRequests, reason);

        return ex.ErrorType switch
        {
            "INVALID_REQUEST" or "INVALID_INPUT" => BadRequest(reason),
            "ITEM_ERROR" => Conflict(reason),
            "ASSET_REPORT_ERROR" => Conflict(reason),
            _ => Error(StatusCodes.Status422UnprocessableEntity, reason)
        };
    }

    /// <summary>The caller's identity from the JWT, or null when it carries none.</summary>
    public static string? UserName(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;

    public static string OwnerKey(string userName) => userName.Trim().ToUpperInvariant();
}
