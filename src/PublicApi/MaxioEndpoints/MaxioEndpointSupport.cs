using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

internal static class MaxioEndpointSupport
{
    /// <summary>Runs a billing operation inside the total budget; a refusal becomes a 4xx/5xx with a reason.</summary>
    public static async Task<IResult> RunAsync<T>(MaxioBillingService billing, HttpContext http,
        Func<CancellationToken, Task<T>> work, Func<T, IResult>? ok = null)
    {
        try
        {
            var result = await billing.Bounded(work, http.RequestAborted);
            return ok != null ? ok(result) : Results.Ok(result);
        }
        catch (BillingException ex)
        {
            return Results.Json(new { statusCode = ex.StatusCode, message = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    public static string? UserName(ClaimsPrincipal user) => user.Identity?.Name;

    public static IResult Unauthenticated() =>
        Results.Json(new { statusCode = 401, message = "No signed-in user." }, statusCode: 401);
}
