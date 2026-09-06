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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's own subscriptions.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, Subscriber, ISubscriptionBillingService, CancellationToken>
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
            (ClaimsPrincipal user,
             ISubscriberAccessor subscribers,
             ISubscriptionBillingService billing,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await subscribers.GetSubscriberAsync(user, cancellationToken);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse
        {
            CustomerReference = subscriber.Reference
        };

        var subscriptions = await billing.ListSubscriptionsAsync(subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
