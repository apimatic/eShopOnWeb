using BlazorShared.Models;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ErrorResult
{
    public static ObjectResult Create(int statusCode, string message)
    {
        return new ObjectResult(new ErrorDetails
        {
            StatusCode = statusCode,
            Message = message
        })
        {
            StatusCode = statusCode
        };
    }
}
