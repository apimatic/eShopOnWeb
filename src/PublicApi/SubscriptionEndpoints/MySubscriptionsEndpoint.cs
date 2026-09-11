using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionResponse
{
    public int? Id { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; }
    public string NextBillingDate { get; set; }
}

public class MySubscriptionsEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioSubscriptionService svc, HttpContext ctx) =>
            {
                var email = ctx.User?.Identity?.Name ?? ctx.User?.FindFirst(ClaimTypes.Email)?.Value;
                if (string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
                var subs = await svc.GetMySubscriptionsAsync(email);
                var resp = new System.Collections.Generic.List<MySubscriptionResponse>();
                foreach (var s in subs)
                {
                    resp.Add(new MySubscriptionResponse
                    {
                        Id = s.Id,
                        PlanHandle = s.PlanHandle,
                        PlanName = s.PlanName,
                        Price = s.PriceInCents / 100m,
                        State = s.State,
                        NextBillingDate = s.NextBillingDate
                    });
                }
                return Results.Ok(resp);
            })
            .Produces<System.Collections.Generic.List<MySubscriptionResponse>>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }
}
