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
/// Lists the authenticated caller's own Maxio subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, ClaimsPrincipal user) =>
            {
                var request = new MySubscriptionsRequest { BuyerId = user.Identity?.Name ?? string.Empty };
                return await HandleAsync(request, billingService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.GetSubscriptionsForBuyerAsync(request.BuyerId);
        response.Subscriptions.AddRange(subscriptions.Select(s => new CustomerSubscriptionDto
        {
            SubscriptionId = s.SubscriptionId,
            PlanHandle = s.PlanHandle,
            PlanName = s.PlanName,
            PriceInCents = s.PriceInCents,
            State = s.State,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextBillingAt = s.NextAssessmentAt,
            CreatedAt = s.CreatedAt
        }));

        return Results.Ok(response);
    }
}
