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
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the calling user's subscriptions, as recorded in Maxio
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionsQuery, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, UserManager<ApplicationUser> userManager,
                ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var user = await BillingUserResolver.ResolveAsync(principal, userManager);
                if (user is null)
                    return Results.Unauthorized();

                return await HandleAsync(new MySubscriptionsQuery(user, cancellationToken), billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsQuery query, ISubscriptionBillingService billingService)
    {
        var response = new ListMySubscriptionsResponse();

        var subscriptions = await billingService.GetSubscriptionsAsync(query.User, query.CancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.From));
        return Results.Ok(response);
    }
}

public record MySubscriptionsQuery(BillingUser User, CancellationToken CancellationToken);
