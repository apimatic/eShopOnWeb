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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions held by the authenticated shopper.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;
    private readonly SubscriberIdentityResolver _subscriberIdentityResolver;

    public MySubscriptionListEndpoint(IMapper mapper, SubscriberIdentityResolver subscriberIdentityResolver)
    {
        _mapper = mapper;
        _subscriberIdentityResolver = subscriberIdentityResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        HandleAsync(user: null, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal? user, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var subscriber = await _subscriberIdentityResolver.ResolveAsync(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
