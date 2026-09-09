using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products in the configured product family)
/// that the logged-in shopper can subscribe to.
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var plans = await _subscriptionService.ListPlansAsync();

        var response = new SubscriptionPlanListResponse
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                Price = p.PriceInCents / 100m,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                HasTrial = p.HasTrial,
                RequireCreditCard = p.RequireCreditCard
            }).ToList()
        };

        return Results.Ok(response);
    }
}
