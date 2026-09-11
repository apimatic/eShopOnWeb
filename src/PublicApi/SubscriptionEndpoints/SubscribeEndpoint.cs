using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest req, ISubscriptionService svc, HttpContext ctx) =>
        {
            return await HandleWithContext(req, svc, ctx);
        })
        .Produces<SubscribeResponse>()
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService svc)
    {
        // Fallback if called without HttpContext; should not happen in route
        return Results.BadRequest(new SubscribeResponse(request.CorrelationId()) { Success = false, Message = "Context missing." });
    }

    private async Task<IResult> HandleWithContext(SubscribeRequest request, ISubscriptionService svc, HttpContext ctx)
    {
        var response = new SubscribeResponse(request.CorrelationId());
        var userName = ctx.User?.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            response.Success = false;
            response.Message = "User identity missing.";
            return Results.BadRequest(response);
        }
        var result = await svc.SubscribeAsync(userName, request.ProductHandle ?? "eshop-pro");
        response.Success = result.Success;
        response.CustomerReference = result.CustomerReference;
        response.CustomerId = result.CustomerId;
        response.SubscriptionId = result.SubscriptionId;
        response.State = result.State;
        response.NextBillingDate = result.NextBillingDate;
        response.PlanHandle = result.PlanHandle;
        response.Message = result.Message;
        return result.Success ? Results.Ok(response) : Results.BadRequest(response);
    }
}

public class SubscribeRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public bool Success { get; set; }
    public string CustomerReference { get; set; } = "";
    public int CustomerId { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public string PlanHandle { get; set; } = "";
    public string Message { get; set; } = "";
}
