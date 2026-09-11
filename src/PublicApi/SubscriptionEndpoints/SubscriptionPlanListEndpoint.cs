using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioBillingService svc) =>
        {
            var family = await svc.GetSubscriptionPlansAsync();
            var plans = new[]
            {
                new { handle = "eshop-pro", name = "Pro Plan", price = 299.00m, interval = "month", frequency = 1, id = 7126957 },
                new { handle = "basic-plan", name = "Basic Plan", price = 29.00m, interval = "month", frequency = 1, id = 7126958 }
            };
            return Results.Ok(new { plans, family = family });
        })
        .Produces(200)
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public Task<IResult> HandleAsync(IMaxioBillingService request) => Task.FromResult<IResult>(Results.Ok());
}
