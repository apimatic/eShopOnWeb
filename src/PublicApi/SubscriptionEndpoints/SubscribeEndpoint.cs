using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscribeEndpoint(IHttpContextAccessor accessor) => _accessor = accessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscribeRequest req, ISubscriptionService svc) => await HandleAsync(req, svc))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service)
    {
        var user = _accessor.HttpContext?.User;
        var reference = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.Identity?.Name
            ?? user?.FindFirst("sub")?.Value
            ?? "anon";

        try
        {
            var result = await service.SubscribeAsync(reference, request.PlanHandle ?? "eshop-pro");
            return Results.Ok(new SubscribeResponse
            {
                SubscriptionId = result.Id,
                State = result.State,
                PlanHandle = result.PlanHandle,
                Price = result.Price,
                NextBillingDate = result.NextBillingDate,
                Message = "Subscription created/confirmed."
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new SubscribeResponse
            {
                Message = $"Subscription failed: {ex.Message}"
            });
        }
    }
}
