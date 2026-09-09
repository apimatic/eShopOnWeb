using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's Maxio subscriptions.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, ApplicationUser, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                   IMaxioSubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                var user = await principal.ResolveApplicationUserAsync(userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(user, subscriptionService, cancellationToken);
            })
           .Produces<MySubscriptionsResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ApplicationUser user, IMaxioSubscriptionService subscriptionService)
    {
        return HandleAsync(user, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ApplicationUser user, IMaxioSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await subscriptionService.GetMySubscriptionsAsync(user.ToMaxioUserInfo(), cancellationToken);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions.ToList() });
        }
        catch (MaxioBillingException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.RecommendedHttpStatusCode);
        }
    }
}
