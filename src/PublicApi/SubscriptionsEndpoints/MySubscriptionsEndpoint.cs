using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>
/// List My Subscriptions (GET api/my-subscriptions)
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionManager>
{
    private readonly IMapper _mapper;

    public MySubscriptionsEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionManager subscriptionManager) =>
            {
                return await HandleAsync(user, subscriptionManager);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionsEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionManager subscriptionManager)
    {
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.Identity.Name))
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse();

        var subscriptions = await subscriptionManager.GetCustomerSubscriptionsAsync(user.Identity.Name);
        response.Subscriptions.AddRange(_mapper.Map<List<SubscriptionDto>>(subscriptions));

        return Results.Ok(response);
    }
}
