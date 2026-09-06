using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, Subscriber, ISubscriptionService>
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
            (ClaimsPrincipal principal,
             UserManager<ApplicationUser> userManager,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(principal, userManager);
                if (subscriber is null)
                {
                    // Authenticated with a token whose user no longer exists.
                    return Results.Unauthorized();
                }

                return await HandleAsync(subscriber, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionService subscriptionService) =>
        HandleAsync(subscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(Subscriber subscriber, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var result = await subscriptionService.ListSubscriptionsAsync(subscriber, cancellationToken);
        if (!result.IsSuccess)
        {
            return SubscriptionResults.Problem(result);
        }

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(result.Value!.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
