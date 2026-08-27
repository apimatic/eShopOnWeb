using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                IShopperIdentityResolver identityResolver,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var shopper = await identityResolver.ResolveAsync(httpContext.User, cancellationToken);
                var subscriptions = await billingService.ListMySubscriptionsAsync(shopper, cancellationToken);
                return Results.Ok(subscriptions.Select(SubscriptionDto.From));
            })
            .Produces<SubscriptionDto[]>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}
