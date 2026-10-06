using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps <see cref="BillingException"/>s from the billing integration onto HTTP responses.
/// </summary>
public static class BillingErrorMapper
{
    public static IResult Map(BillingException exception)
    {
        var statusCode = exception.StatusCode switch
        {
            (int)System.Net.HttpStatusCode.NotFound => (int)System.Net.HttpStatusCode.NotFound,
            (int)System.Net.HttpStatusCode.BadRequest or (int)System.Net.HttpStatusCode.UnprocessableEntity => (int)System.Net.HttpStatusCode.BadRequest,
            _ => (int)System.Net.HttpStatusCode.BadGateway
        };

        return Results.Json(
            new { statusCode, message = exception.Message },
            statusCode: statusCode);
    }
}