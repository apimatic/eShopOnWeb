using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (ClaimsPrincipal user, IMaxioBillingService svc) =>
        {
            var reference = user.FindFirst("email")?.Value ?? user.Identity?.Name ?? "unknown";
            var subs = await svc.GetSubscriptionsByCustomerReferenceAsync(reference);
            return Results.Ok(new { reference, subscriptions = subs });
        })
        .Produces(200)
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public Task<IResult> HandleAsync(IMaxioBillingService request) => Task.FromResult<IResult>(Results.Ok());
}
