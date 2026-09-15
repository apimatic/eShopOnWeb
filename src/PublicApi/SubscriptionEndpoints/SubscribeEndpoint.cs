using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Creates or reconciles the caller's subscription enrollment.</summary>
public sealed class SubscribeEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    // HTTP request binding supplies the authenticated shopper and request payload in AddRoute.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status500InternalServerError));

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (
                SubscribeRequest request,
                ISubscriptionBillingService billing,
                UserManager<ApplicationUser> userManager,
                HttpContext context) =>
            {
                var shopper = await SubscriptionEndpointSupport.GetShopperAsync(context, userManager, context.RequestAborted);
                var result = await billing.SubscribeAsync(shopper, request.PlanHandle ?? string.Empty, context.RequestAborted);
                var response = new SubscribeResponse(result.Subscription is null ? null : SubscriptionEndpointSupport.ToResponse(result.Subscription),
                    result.IsPending, result.Reference);
                return result.IsPending ? Results.Accepted("/api/my-subscriptions", response) : Results.Ok(response);
            })
            .RequireAuthorization("PublicApiJwt")
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status202Accepted)
            .WithTags("Subscriptions");
    }
}
