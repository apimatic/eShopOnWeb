using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.eShopWeb.PublicApi;
using System;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req, IMaxioBillingService svc, HttpContext ctx) => { req.UserName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.Identity?.Name ?? "unknown"; return await HandleAsync(req, svc); })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService svc)
    {
        string email = request.Email ?? request.UserName;
        var sub = await svc.SubscribeAsync(request.UserName, email, request.PlanHandle);
        if (sub == null)
            return Results.BadRequest(new SubscribeResponse { Success = false, Message = "Subscription failed." });

        return Results.Ok(new SubscribeResponse
        {
            Success = true,
            SubscriptionId = sub.Id,
            PlanHandle = sub.ProductHandle,
            State = sub.State,
            NextBillingAt = sub.NextBillingAt,
            CustomerId = sub.CustomerId
        });
    }
}

public class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = "";
    public string? Email { get; set; }
    public string UserName { get; set; } = "";
}

public class SubscribeResponse : BaseResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? NextBillingAt { get; set; }
    public int CustomerId { get; set; }
}
