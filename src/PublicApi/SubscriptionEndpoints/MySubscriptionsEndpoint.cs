using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the calling user's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, HttpContext, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public MySubscriptionsEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(httpContext, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsResponse();

        var userId = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(userId, httpContext.RequestAborted);

        response.Subscriptions = _mapper.Map<List<SubscriptionDto>>(subscriptions);

        return Results.Ok(response);
    }
}
