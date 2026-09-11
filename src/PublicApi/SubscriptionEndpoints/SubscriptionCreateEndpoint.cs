using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (ClaimsPrincipal user, IMaxioBillingService svc, HttpContext ctx) =>
        {
            var email = user.FindFirst("email")?.Value ?? user.Identity?.Name ?? "unknown";
            var reference = email;

            // Idempotent customer creation
            var customer = await svc.GetCustomerByReferenceAsync(reference);
            if (customer == null)
            {
                customer = await svc.CreateCustomerAsync(email, reference);
            }

            // Extract product info from request body if needed; for simplicity assume Pro Plan (eshop-pro)
            int productFamilyId = 3023074;
            int productId = 7126957;

            var sub = await svc.CreateSubscriptionAsync(reference, productFamilyId, productId, email);
            return Results.Ok(new { customer, subscription = sub, reference });
        })
        .Produces(200)
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public Task<IResult> HandleAsync(IMaxioBillingService request) => Task.FromResult<IResult>(Results.Ok());
}
