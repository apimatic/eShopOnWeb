using BlazorShared.Models;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderEndpointResults
{
    /// <summary>The same error shape the API's exception middleware writes.</summary>
    public static IResult Error(int statusCode, string message) =>
        Results.Json(new ErrorDetails { StatusCode = statusCode, Message = message }, statusCode: statusCode);
}
