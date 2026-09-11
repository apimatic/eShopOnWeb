using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpCtx;
    public MySubscriptionListEndpoint(IHttpContextAccessor httpCtx) { _httpCtx = httpCtx; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (IMaxioBillingService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingService service)
    {
        var user = _httpCtx.HttpContext?.User;
        var userRef = user?.Identity?.Name ?? user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.FindFirst("sub")?.Value ?? "unknown";
        var subs = await service.GetMySubscriptionsAsync(userRef);
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions = subs.Select(s => new MySubscriptionDto
        {
            Id = s.Id,
            PlanHandle = s.PlanHandle,
            State = s.State,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
        }).ToList();
        return Results.Ok(response);
    }
}
