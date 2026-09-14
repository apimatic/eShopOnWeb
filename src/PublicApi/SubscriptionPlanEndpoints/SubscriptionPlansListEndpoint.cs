using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// List Subscription Plans (GET api/subscription-plans)
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult, ISubscriptionManager>
{
    private readonly IMapper _mapper;

    public SubscriptionPlansListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionManager subscriptionManager) =>
            {
                return await HandleAsync(subscriptionManager);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionManager subscriptionManager)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionManager.GetSubscriptionPlansAsync();
        response.SubscriptionPlans.AddRange(_mapper.Map<SubscriptionPlanDto[]>(plans).OrderBy(p => p.PriceInCents));

        return Results.Ok(response);
    }
}
