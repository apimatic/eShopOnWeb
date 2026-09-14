using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's Maxio subscriptions. Maxio is the system of record, so this
/// always reflects live state rather than a local cache.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IMaxioService maxioService) =>
            {
                return await HandleAsync(user, maxioService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IMaxioService maxioService)
    {
        var response = new ListMySubscriptionsResponse();

        var reference = SubscriberIdentity.GetReference(user);
        if (string.IsNullOrEmpty(reference))
        {
            return Results.Unauthorized();
        }

        var customer = await maxioService.FindCustomerByReferenceAsync(reference);
        if (customer is null)
        {
            // No Maxio customer yet means no subscriptions yet - not an error.
            return Results.Ok(response);
        }

        var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id);
        response.Subscriptions.AddRange(subscriptions.Select(ToDto));

        return Results.Ok(response);
    }

    internal static SubscriptionDto ToDto(MaxioSubscription s) => new()
    {
        Id = s.Id,
        State = s.State,
        PlanHandle = s.ProductHandle,
        PlanName = s.ProductName,
        Price = s.ProductPriceInCents.HasValue ? s.ProductPriceInCents.Value / 100m : null,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingAt = s.NextAssessmentAt
    };
}
