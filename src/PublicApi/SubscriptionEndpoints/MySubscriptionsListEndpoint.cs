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
/// Lists the calling user's subscriptions (identity taken from the JWT). Returns an empty list if
/// the user has never subscribed (no Maxio customer exists yet).
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, string, ISubscriptionService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             ClaimsPrincipal principal,
             CancellationToken cancellationToken) =>
            {
                var registration = await SubscriberIdentity.ResolveAsync(principal, userManager);
                if (registration is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(registration.Reference, subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(string customerReference, ISubscriptionService subscriptionService, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriptions = await subscriptionService.GetSubscriptionsAsync(customerReference, cancellationToken);
            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => s.ToDto()).ToList(),
            };
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionErrorResults.FromMaxio(ex);
        }
    }
}
