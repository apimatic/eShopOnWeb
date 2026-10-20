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
/// List the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> users, ISubscriptionBilling billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, users, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users, ISubscriptionBilling billing, CancellationToken cancellationToken)
    {
        var appUser = await SubscriptionHttp.ResolveUserAsync(principal, users);
        if (appUser == null)
            return Results.Unauthorized();

        var response = new ListMySubscriptionsResponse();
        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(appUser.Id, cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));
            return Results.Ok(response);
        }
        catch (BillingException ex)
        {
            return SubscriptionHttp.ToResult(ex);
        }
    }
}
