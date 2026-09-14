using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// Lists the subscription plans available for subscription
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListSubscriptionPlansEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await subscriptionService.ListPlansAsync(CancellationToken.None);

        response.Plans = _mapper.Map<List<SubscriptionPlanDto>>(plans);

        return Results.Ok(response);
    }
}
