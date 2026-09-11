using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public SubscriptionEndpoint(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest req, IMaxioBillingService svc) =>
        {
            var ctx = _httpContextAccessor.HttpContext!;
            var userName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.Identity?.Name ?? "anonymous";
            var email = userName; // in this app username approximates identity
            var result = await svc.SubscribeAsync(userName, email, req.ProductHandle);
            return Results.Ok(new SubscribeResponse(req.CorrelationId())
            {
                Success = result.Success,
                PlanHandle = result.PlanHandle,
                State = result.State,
                NextBillingDate = result.NextBillingDate,
                Error = result.Error
            });
        })
        .Produces<SubscribeResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioBillingService svc)
    {
        var ctx = _httpContextAccessor.HttpContext!;
        var userName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.Identity?.Name ?? "anonymous";
        var email = userName;
        var result = await svc.SubscribeAsync(userName, email, request.ProductHandle);
        return Results.Ok(new SubscribeResponse(request.CorrelationId())
        {
            Success = result.Success,
            PlanHandle = result.PlanHandle,
            State = result.State,
            NextBillingDate = result.NextBillingDate,
            Error = result.Error
        });
    }
}
