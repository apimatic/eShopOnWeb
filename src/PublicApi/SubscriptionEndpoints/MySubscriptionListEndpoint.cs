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
/// List the caller's own subscriptions.
/// </summary>
/// <remarks>
/// Endpoint instances are created once, when routes are mapped, so per-request services are taken as
/// route handler parameters rather than through the constructor.
/// </remarks>
public class MySubscriptionListEndpoint : IEndpoint
{
    private readonly IMapper _mapper;

    public MySubscriptionListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService subscriptionBillingService, SubscriberAccountResolver accountResolver,
                ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(subscriptionBillingService, accountResolver, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService subscriptionBillingService,
        SubscriberAccountResolver accountResolver, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        // Only ever the caller's own subscriptions: the account comes from the bearer token.
        var account = await accountResolver.ResolveAsync(user, cancellationToken);
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionBillingService.ListSubscriptionsAsync(account, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
