using System;
using System.Security.Claims;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, object, ISubscriptionService>
{
    private readonly IHttpContextAccessor _accessor;
    public MySubscriptionsEndpoint(IHttpContextAccessor accessor) => _accessor = accessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ISubscriptionService svc) => await HandleAsync(new object(), svc))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(object request, ISubscriptionService service)
    {
        var user = _accessor.HttpContext?.User;
        var reference = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.Identity?.Name
            ?? user?.FindFirst("sub")?.Value
            ?? "anon";
        try
        {
            var results = await service.GetMySubscriptionsAsync(reference);
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(results.Select(r => new MySubscriptionDto
            {
                Id = r.Id,
                State = r.State,
                PlanHandle = r.PlanHandle,
                Price = r.Price,
                NextBillingDate = r.NextBillingDate
            }));
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
