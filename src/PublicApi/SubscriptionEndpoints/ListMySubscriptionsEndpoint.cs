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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var user = await BillingEndpointUser.ResolveAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await billingService.ListSubscriptionsAsync(user.Id, cancellationToken);
                return Results.Ok(new ListMySubscriptionsResponse(
                    subscriptions.Select(SubscriptionDto.From).ToList()));
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
