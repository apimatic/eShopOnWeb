using System.Linq;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using BlazorShared.Models;

namespace Microsoft.eShopWeb.PublicApi.Extensions;

internal static class BillingResultExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToFailureResult<T>(this Result<T> result)
    {
        if (result.Status == ResultStatus.Invalid)
        {
            var message = result.ValidationErrors.FirstOrDefault()?.ErrorMessage
                          ?? result.Errors.FirstOrDefault()
                          ?? "The request is invalid.";
            return Results.BadRequest(new ErrorDetails { StatusCode = StatusCodes.Status400BadRequest, Message = message });
        }

        if (result.Status == ResultStatus.NotFound)
        {
            var message = result.Errors.FirstOrDefault() ?? "The requested resource was not found.";
            return Results.NotFound(new ErrorDetails { StatusCode = StatusCodes.Status404NotFound, Message = message });
        }

        var error = result.Errors.FirstOrDefault() ?? "Billing is temporarily unavailable.";
        return Results.Json(
            new ErrorDetails { StatusCode = StatusCodes.Status502BadGateway, Message = error },
            statusCode: StatusCodes.Status502BadGateway);
    }

    public static SubscriptionEndpoints.SubscriptionDto ToDto(this ApplicationCore.Billing.ShopperSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate
        };
}
