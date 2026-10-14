using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (MaxioSubscriptionService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetPlansAsync(cancellationToken)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).WithTags("Subscriptions").Produces<SubscriptionPlansResponse>();

        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.SubscribeAsync(user, request.PlanHandle, cancellationToken)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).WithTags("Subscriptions").Produces<SubscriptionDto>();

        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, MaxioSubscriptionService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.GetMySubscriptionsAsync(user, cancellationToken)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme }).WithTags("Subscriptions").Produces<MySubscriptionsResponse>();
    }
}
