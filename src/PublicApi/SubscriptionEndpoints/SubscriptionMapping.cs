using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Mapping helpers between the ApplicationCore subscription models and the PublicApi DTOs, plus
/// translation of <see cref="Ardalis.Result.Result"/> outcomes into HTTP results.
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        FormattedPrice = plan.Price.ToString("C2", CultureInfo.GetCultureInfo("en-US")),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        PricePointName = plan.PricePointName
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        ProductPriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPrice,
        FormattedPrice = subscription.ProductPrice.ToString("C2", CultureInfo.GetCultureInfo("en-US")),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference,
        CustomerId = subscription.CustomerId
    };

    /// <summary>
    /// Resolves the authenticated caller's stable identity from the JWT. eShopOnWeb issues tokens
    /// whose name claim is the user's login name (email), which is used as the Maxio customer reference.
    /// </summary>
    public static EShopSubscriber? ToSubscriber(this ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name
                       ?? principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.FindFirstValue("unique_name");

        return string.IsNullOrWhiteSpace(userName) ? null : EShopSubscriber.FromUserName(userName);
    }

    /// <summary>
    /// Maps a failed <see cref="Ardalis.Result.Result"/> status to an appropriate HTTP problem
    /// response. Upstream Maxio failures surface as 502 Bad Gateway; validation as 422; not-found as 404.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToProblem(this Ardalis.Result.IResult result)
    {
        var errors = result.Errors.Any() ? result.Errors.ToArray() : new[] { "The billing request could not be completed." };
        var detail = string.Join("; ", errors);

        return result.Status switch
        {
            ResultStatus.NotFound => Results.Problem(detail: detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found"),
            ResultStatus.Invalid => Results.Problem(
                detail: string.Join("; ", result.ValidationErrors.Select(e => e.ErrorMessage).DefaultIfEmpty(detail)),
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Invalid Subscription Request"),
            _ => Results.Problem(detail: detail, statusCode: StatusCodes.Status502BadGateway, title: "Billing Provider Error")
        };
    }
}
