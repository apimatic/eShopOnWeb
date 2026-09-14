using System.Linq;
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
/// Lists the authenticated shopper's subscriptions, read live from the billing system of record.
/// Live subscriptions come first, then the most recent.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ICurrentSubscriber, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public ListMySubscriptionsEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ICurrentSubscriber currentSubscriber, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(currentSubscriber, subscriptionService, cancellationToken))
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ICurrentSubscriber currentSubscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(currentSubscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ICurrentSubscriber currentSubscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var subscriber = await currentSubscriber.GetAsync();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
