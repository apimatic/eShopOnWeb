using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Lists the authenticated shopper's live Maxio subscriptions.</summary>
public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    // HTTP request binding supplies the authenticated shopper in AddRoute.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status500InternalServerError));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (
                ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                HttpContext context) =>
            {
                var shopper = await SubscriptionEndpointSupport.GetShopperAsync(context, userManager, context.RequestAborted);
                var subscriptions = await billing.ListMySubscriptionsAsync(shopper, context.RequestAborted);
                return Results.Ok(new MySubscriptionsResponse(subscriptions.Select(SubscriptionEndpointSupport.ToResponse).ToList()));
            })
            .RequireAuthorization("PublicApiJwt")
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }
}
