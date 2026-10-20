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
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the authenticated shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, BillingBuyer, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, UserManager<ApplicationUser> userManager, ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var buyer = await SubscriptionMapping.ResolveBuyerAsync(user, userManager);
                if (buyer is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(buyer, billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(BillingBuyer buyer, ISubscriptionBillingService billingService) =>
        HandleAsync(buyer, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(BillingBuyer buyer, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse();

        var result = await billingService.GetSubscriptionsAsync(buyer, cancellationToken);

        response.Subscriptions.AddRange(result.Subscriptions.Select(SubscriptionMapping.ToDto));
        response.PendingPlanHandles.AddRange(result.PendingPlanHandles);

        return Results.Ok(response);
    }
}
