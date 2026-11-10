using System;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

internal static class StudioResults
{
    /// <summary>Runs a studio operation; a request the app cannot carry out answers with its status and reason.</summary>
    public static async Task<IResult> RunAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (StudioException ex)
        {
            return Results.Json(new ErrorDetails { StatusCode = ex.StatusCode, Message = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    public static string RequireUser(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? throw new StudioException(401, "The token carries no user name.") : userName;
}
