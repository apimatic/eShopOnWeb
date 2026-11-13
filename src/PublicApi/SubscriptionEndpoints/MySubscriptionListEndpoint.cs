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
/// Lists the calling user's subscriptions, read live from Maxio.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, SubscriptionCallContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billing,
             CancellationToken cancellationToken) =>
            {
                var user = await BillingUserResolver.ResolveAsync(principal, userManager);
                return await HandleAsync(new SubscriptionCallContext(user, billing, cancellationToken));
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCallContext context)
    {
        var user = context.User;
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await context.Billing.ListSubscriptionsAsync(user, context.CancellationToken);
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));
        return Results.Ok(response);
    }
}
