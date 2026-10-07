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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, userManager, subscriptionService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var subscriber = await SubscriberIdentityResolver.ResolveAsync(user, userManager);
        if (subscriber == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(subscriber, cancellationToken);
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDtoMapper.ToDto));
            return Results.Ok(response);
        }
        catch (BillingProviderException ex)
        {
            return SubscriptionErrorResults.From(ex);
        }
    }
}
