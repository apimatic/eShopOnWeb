using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class BillingResults
{
    public record ErrorBody(int Status, string Error);

    /// <summary>Runs a billing call, answering a refused request with its 4xx/5xx status and the reason.</summary>
    public static async Task<IResult> RunAsync(Func<Task<IResult>> call)
    {
        try
        {
            return await call();
        }
        catch (MaxioException ex)
        {
            return Results.Json(new ErrorBody(ex.StatusCode, ex.Message), statusCode: ex.StatusCode);
        }
    }
}
