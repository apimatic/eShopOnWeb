using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions, as reflected in Maxio. Empty when the shopper
/// has not subscribed to anything yet.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                CancellationToken cancellationToken) =>
            {
                var user = await SubscriptionEndpointHelpers.ResolveBillingUserAsync(principal, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                try
                {
                    var subscriptions = await billing.GetMySubscriptionsAsync(user, cancellationToken);
                    var response = new MySubscriptionsResponse();
                    response.Subscriptions.AddRange(subscriptions);
                    return Results.Ok(response);
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionEndpointHelpers.ToProblem(ex);
                }
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
