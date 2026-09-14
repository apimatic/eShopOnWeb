using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's subscriptions. Returns an empty list if they have never subscribed.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IBillingService billingService) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(user.Identity!.Name!), billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IBillingService billingService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.GetSubscriptionsForCustomerAsync(request.CustomerReference);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            SubscriptionId = s.BillingSubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            Price = s.PriceInCents / 100m,
            State = s.State,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAtUtc,
            NextBillingAt = s.NextBillingAtUtc
        }));

        return Results.Ok(response);
    }
}
