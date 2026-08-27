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

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             HttpContext context,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var user = await BillingEndpointUser.ResolveAsync(context, userManager);
                var result = await billingService.SubscribeAsync(user, request.ProductHandle, cancellationToken);
                return result.InProgress
                    ? Results.Accepted("/api/my-subscriptions", result)
                    : Results.Ok(result);
            })
            .Produces<SubscriptionDetails>(StatusCodes.Status200OK)
            .Produces<SubscriptionDetails>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("Subscriptions");
    }
}
