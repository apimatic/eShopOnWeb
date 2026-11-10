using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Turns the outcome of a studio operation into an HTTP result: success, or a status with the reason.
/// </summary>
public static class StudioResults
{
    public static async Task<IResult> RunAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (StudioException ex)
        {
            return Failure(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(MindbodyGateway.Translate(ex));
        }
    }

    private static IResult Failure(StudioException ex) =>
        Results.Json(new StudioErrorResponse(ex.StatusCode, ex.Message), statusCode: ex.StatusCode);
}
