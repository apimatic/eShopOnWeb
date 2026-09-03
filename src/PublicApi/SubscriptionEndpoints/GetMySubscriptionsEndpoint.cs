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

public sealed class GetMySubscriptionsEndpoint : IEndpoint
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
                    var user = await BillingUserFactory.FromPrincipalAsync(principal, userManager);
                    if (user is null)
                    {
                        return Results.Unauthorized();
                    }

                    var subscriptions = await billingService.ListSubscriptionsAsync(user, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse(
                        subscriptions.Select(SubscriptionDto.From).ToArray()));
                })
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}
