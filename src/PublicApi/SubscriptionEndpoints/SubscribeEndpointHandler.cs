using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpCtx;
    public SubscribeEndpoint(IHttpContextAccessor httpCtx) { _httpCtx = httpCtx; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (SubscribeRequest req, IMaxioBillingService service) =>
            {
                return await HandleAsync(req, service);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest req, IMaxioBillingService service)
    {
        var user = _httpCtx.HttpContext?.User;
        var userRef = user?.Identity?.Name ?? user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.FindFirst("sub")?.Value ?? "unknown";
        var result = await service.SubscribeAsync(userRef, req.PlanHandle ?? "eshop-pro");
        return Results.Ok(new SubscribeResponse
        {
            SubscriptionId = result.Id,
            PlanHandle = result.PlanHandle,
            State = result.State,
            NextBillingAt = result.NextAssessmentAt,
            CurrentPeriodEndsAt = result.CurrentPeriodEndsAt
        });
    }
}
