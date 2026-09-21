using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// GET /api/my-subscriptions — lists the authenticated shopper's subscriptions. JWT-authenticated.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing,
             UserManager<ApplicationUser> userManager,
             ClaimsPrincipal principal,
             CancellationToken ct) =>
            {
                var user = await SubscriptionEndpointHelpers.ResolveCurrentUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await billing.GetSubscriptionsAsync(user.Id, ct);
                    return Results.Ok(new MySubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(SubscriptionEndpointHelpers.ToDto).ToList()
                    });
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionEndpointHelpers.ToProblem(ex);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags(SubscriptionEndpointHelpers.Tag);
    }
}
