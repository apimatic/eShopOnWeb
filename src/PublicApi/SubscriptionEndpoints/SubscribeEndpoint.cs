using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string PlanHandle { get; set; }
}

public class SubscribeResponse
{
    public int? SubscriptionId { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; }
    public string NextBillingDate { get; set; }
}

public class SubscribeEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req, IMaxioSubscriptionService svc, HttpContext ctx) =>
            {
                var email = ctx.User?.Identity?.Name ?? ctx.User?.FindFirst(ClaimTypes.Email)?.Value;
                if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
                var sub = await svc.SubscribeAsync(email, req.PlanHandle);
                return Results.Ok(new SubscribeResponse
                {
                    SubscriptionId = sub.Id,
                    PlanHandle = sub.PlanHandle,
                    PlanName = sub.PlanName,
                    Price = sub.PriceInCents / 100m,
                    State = sub.State,
                    NextBillingDate = sub.NextBillingDate
                });
            })
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }
}
