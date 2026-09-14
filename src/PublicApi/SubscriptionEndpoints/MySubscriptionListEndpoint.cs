using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the signed-in shopper's own subscriptions, with plan, price, state and next billing date.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, HttpContext, SubscriberResolver>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly IMapper _mapper;

    public MySubscriptionListEndpoint(ISubscriptionBillingService billingService, IMapper mapper)
    {
        _billingService = billingService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, SubscriberResolver subscriberResolver) =>
            {
                return await HandleAsync(httpContext, subscriberResolver);
            })
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, SubscriberResolver subscriberResolver)
    {
        var response = new MySubscriptionListResponse();

        var subscriber = await subscriberResolver.GetSubscriberAsync(httpContext.User);
        var subscriptions = await _billingService.ListSubscriptionsAsync(subscriber, httpContext.RequestAborted);

        response.CustomerReference = subscriber.Reference;
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
