using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (ISubscriptionService service, CancellationToken cancellationToken) =>
            {
                var response = new SubscriptionPlansResponse();
                response.Plans.AddRange(await service.GetPlansAsync(cancellationToken));
                return Results.Ok(response);
            })
            .RequireAuthorization(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
