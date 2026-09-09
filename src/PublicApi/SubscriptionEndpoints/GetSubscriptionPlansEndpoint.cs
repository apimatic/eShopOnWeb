using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products) available to subscribe to.
/// </summary>
public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, GetSubscriptionPlansRequest, IMaxioSubscriptionService>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService subscriptionService) =>
                await HandleAsync(new GetSubscriptionPlansRequest(), subscriptionService))
            .RequireAuthorization()
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetSubscriptionPlansRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new GetSubscriptionPlansResponse(request.CorrelationId());

        var plans = await subscriptionService.GetPlansAsync();
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            ProductFamilyHandle = p.ProductFamilyHandle
        }));

        return Results.Ok(response);
    }
}

/// <summary>Empty request marker for the plans list endpoint (no body needed).</summary>
public class GetSubscriptionPlansRequest : BaseRequest
{
}
