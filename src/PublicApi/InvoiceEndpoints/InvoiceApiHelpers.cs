using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Shared helpers for the invoicing endpoints: turning the token into a <see cref="CallerContext"/> and
/// mapping a non-success <see cref="OperationResult{T}"/> onto the right HTTP response.
/// </summary>
public static class InvoiceApiHelpers
{
    public static CallerContext GetCaller(ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name ?? string.Empty;
        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        return new CallerContext(userName, isAdmin);
    }

    /// <summary>Maps a failed result onto an HTTP response with a caller-safe message.</summary>
    public static IResult ToFailure<T>(OperationResult<T> result)
    {
        var message = result.Message ?? "The request could not be completed.";
        return result.Status switch
        {
            OperationStatus.NotFound => Results.NotFound(new ProblemPayload(message)),
            OperationStatus.Forbidden => Results.Json(new ProblemPayload(message), statusCode: StatusCodes.Status403Forbidden),
            OperationStatus.Invalid => Results.BadRequest(new ProblemPayload(message)),
            OperationStatus.Conflict => Results.Conflict(new ProblemPayload(message)),
            _ => Results.Json(new ProblemPayload(message), statusCode: StatusCodes.Status502BadGateway)
        };
    }

    public sealed record ProblemPayload(string Message);
}
