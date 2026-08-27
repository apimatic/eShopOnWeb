using System.Linq;
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

public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (HttpContext context,
                    ISubscriptionBillingService billing,
                    SubscriptionUserResolver userResolver,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(context, billing, userResolver, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext context,
        ISubscriptionBillingService billing,
        SubscriptionUserResolver userResolver,
        CancellationToken cancellationToken)
    {
        var user = await userResolver.ResolveAsync(context);
        var subscriptions = await billing.GetSubscriptionsAsync(user.UserId, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(subscription => subscription.ToDto()).ToArray()
        });
    }
}
