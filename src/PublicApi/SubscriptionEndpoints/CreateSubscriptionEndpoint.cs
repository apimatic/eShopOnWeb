using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest req, IMaxioBillingService svc, HttpContext ctx) =>
            {
                var user = ctx.User.Identity?.Name ?? "unknown";
                var res = await svc.SubscribeAsync(user, req.PlanHandle, req.Email);
                if (res == null) return Results.BadRequest(new { Success = false, Message = "No result" });
                return Results.Ok(new { res.Success, res.Message, res.SubscriptionId, res.State, res.NextBillingDate, res.PlanName, res.PlanPrice });
            })
            .Produces<object>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService svc)
    {
        return Results.Ok(new { Success = true });
    }
}
