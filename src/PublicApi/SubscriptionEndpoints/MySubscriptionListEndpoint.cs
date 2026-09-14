using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                var user = SubscriptionUserFactory.Create(principal);
                var subscriptions = await billingService.GetSubscriptionsAsync(user, cancellationToken);
                return Results.Ok(subscriptions.Select(SubscriptionDto.From));
            })
            .Produces<SubscriptionDto[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }
}
