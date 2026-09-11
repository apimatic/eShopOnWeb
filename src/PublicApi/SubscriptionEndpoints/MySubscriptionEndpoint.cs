using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionEndpoint : IEndpoint<IResult, BaseRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    public MySubscriptionEndpoint(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (IMaxioBillingService svc) =>
        {
            var ctx = _httpContextAccessor.HttpContext!;
            var userName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.Identity?.Name ?? "anonymous";
            var subs = await svc.GetMySubscriptionsAsync(userName);
            return Results.Ok(subs);
        })
        .Produces<List<MySubscriptionDto>>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(BaseRequest request, IMaxioBillingService svc)
    {
        var ctx = _httpContextAccessor.HttpContext!;
        var userName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.Identity?.Name ?? "anonymous";
        var subs = await svc.GetMySubscriptionsAsync(userName);
        return Results.Ok(subs);
    }
}
