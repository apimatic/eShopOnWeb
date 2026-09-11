using System.Security.Claims;
using System.Linq;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService service)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.Identity?.Name ?? "unknown";
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var customer = await service.FindCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            return Results.Ok(new MySubscriptionsResponse());
        }

        var subs = await service.ListSubscriptionsForCustomerAsync(customer.id);
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(subs.Select(s => new MySubscriptionDto
        {
            Id = s.id,
            State = s.state,
            Price = s.product_price_in_cents / 100m,
            PlanName = s.product?.name ?? string.Empty,
            PlanHandle = s.product?.handle ?? string.Empty,
            NextBillingDate = s.current_period_ends_at ?? string.Empty
        }));
        return Results.Ok(response);
    }
}
