using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (HttpContext httpContext,
                    BillingUserResolver userResolver,
                    ISubscriptionBillingService service,
                    CancellationToken cancellationToken) =>
                {
                    var user = await userResolver.ResolveAsync(httpContext.User, cancellationToken);
                    var subscriptions = await service.GetSubscriptionsAsync(user, cancellationToken);
                    return Results.Ok(new MySubscriptionsResponse(subscriptions.Select(SubscriptionDto.From).ToList()));
                })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }
}
