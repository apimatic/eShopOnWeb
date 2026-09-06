using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Lists the subscriptions belonging to the authenticated caller.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriptionBillingService>
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
            (ClaimsPrincipal user, SubscriberIdentityResolver identityResolver, ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await identityResolver.ResolveAsync(user);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListMySubscriptionsRequest(subscriber) { CancellationToken = cancellationToken }, billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.GetSubscriptionsAsync(request.Subscriber, request.CancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
