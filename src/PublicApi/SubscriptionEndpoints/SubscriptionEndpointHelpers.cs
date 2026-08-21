using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static async Task<ShopperBillingIdentity?> ResolveShopperAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new ShopperBillingIdentity(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            user.UserName);
    }

    public static HttpResult MapResult<T>(Result<T> result, Func<T, HttpResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        return result.Status switch
        {
            ResultStatus.NotFound => Results.NotFound(new { message = FirstError(result) }),
            ResultStatus.Invalid => Results.BadRequest(new
            {
                errors = result.ValidationErrors.Select(e => e.ErrorMessage).ToList()
            }),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            _ => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status502BadGateway)
        };
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        NextBillingAt = subscription.NextBillingAt
    };

    private static string FirstError<T>(Result<T> result)
        => result.Errors.FirstOrDefault()
           ?? result.ValidationErrors.FirstOrDefault()?.ErrorMessage
           ?? "The requested resource was not found.";
}
