using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscriptionService subscriptions, CancellationToken cancellationToken) =>
                Results.Ok(await subscriptions.GetPlansAsync(cancellationToken)))
            .Produces<IReadOnlyList<SubscriptionPlanDto>>()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(SubscriptionService subscriptions) => throw new NotSupportedException();
}
