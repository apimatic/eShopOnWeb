using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (SubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            await HandleAsync(subscriptionService, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.Unauthorized());

    private static async Task<IResult> HandleAsync(SubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new SubscriptionPlansResponse();
        response.Plans.AddRange(await subscriptionService.GetPlansAsync(cancellationToken));
        return Results.Ok(response);
    }
}
