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

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                (ClaimsPrincipal principal,
                    ISubscriptionService subscriptionService,
                    UserManager<ApplicationUser> userManager,
                    CancellationToken cancellationToken) =>
                    HandleAsync(principal, subscriptionService, userManager, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticatedBillingUserResolver.ResolveAsync(principal, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListForUserAsync(user, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionDto.From).ToArray()
        });
    }
}
