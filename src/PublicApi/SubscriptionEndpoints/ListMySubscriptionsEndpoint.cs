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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                MaxioApiClient maxio, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, userManager, maxio, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
        MaxioApiClient maxio, CancellationToken cancellationToken)
    {
        var appUser = await userManager.FindByNameAsync(user.Identity?.Name ?? string.Empty);
        if (appUser is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var customer = await maxio.FindCustomerByReferenceAsync(appUser.Id, cancellationToken);
        if (customer is not null)
        {
            var subscriptions = await maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMapper.ToDto));
        }

        return Results.Ok(response);
    }
}
