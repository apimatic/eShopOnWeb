using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the calling shopper's subscriptions, as held by the billing system
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var buyer = await BuyerIdentity.ResolveAsync(user, userManager);
                return await HandleAsync(new MySubscriptionsRequest(buyer), subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService) =>
        HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        if (request.Buyer is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(request.CorrelationId());

        var mine = await subscriptionService.GetMySubscriptionsAsync(request.Buyer.BuyerId, cancellationToken);
        response.Subscriptions.AddRange(mine.Subscriptions.Select(SubscriptionDto.From));
        response.Pending.AddRange(mine.Pending.Select(p => new PendingSubscriptionDto
        {
            PlanHandle = p.PlanHandle,
            SubscriptionReference = p.SubscriptionReference
        }));

        return Results.Ok(response);
    }
}
