using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Shared plumbing for the billing endpoints: caller identity, overall time budget, error answers.</summary>
public static class BillingEndpointHelper
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

    /// <summary>The caller's identity is the user name carried by the JWT (the only identity claim it has).</summary>
    public static string? CallerName(ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static IResult Failure(int status, string reason) =>
        Results.Problem(detail: reason, statusCode: status, title: reason);

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);

    /// <summary>Runs one request's billing work under a single 45 s budget and turns failures into 4xx/5xx answers.</summary>
    public static async Task<IResult> RunAsync(HttpContext http, Func<CancellationToken, Task<IResult>> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        cts.CancelAfter(Budget);
        try
        {
            return await work(cts.Token);
        }
        catch (BillingException ex)
        {
            return Failure(ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            return Failure(504, "The billing system did not answer in time.");
        }
    }

    public static async Task<IResult> RunForCallerAsync(HttpContext http, Func<string, CancellationToken, Task<IResult>> work)
    {
        var name = CallerName(http.User);
        if (name is null) return Failure(401, "The caller's identity is missing from the token.");
        return await RunAsync(http, ct => work(name, ct));
    }
}
