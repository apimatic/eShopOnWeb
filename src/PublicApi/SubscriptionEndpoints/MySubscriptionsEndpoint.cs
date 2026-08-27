using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var user = await BillingEndpointUser.ResolveAsync(context, userManager);
                var subscriptions = await billingService.ListSubscriptionsAsync(user, cancellationToken);
                return Results.Ok(subscriptions);
            })
            .Produces<SubscriptionDetails[]>(StatusCodes.Status200OK)
            .WithTags("Subscriptions");
    }
}
