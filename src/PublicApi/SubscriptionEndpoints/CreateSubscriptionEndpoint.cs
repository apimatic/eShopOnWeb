using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService, MaxioSettings>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioService service, MaxioSettings settings) =>
            {
                return await HandleAsync(request, service, settings);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService service, MaxioSettings settings)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.Identity?.Name ?? "unknown";
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        // Idempotent customer: lookup by reference
        var existing = await service.FindCustomerByReferenceAsync(userId);
        if (existing == null)
        {
            existing = await service.CreateCustomerAsync(userId, userId, userId, userId);
        }

        var productHandle = !string.IsNullOrWhiteSpace(request.PlanHandle) ? request.PlanHandle : "eshop-pro";
        var sub = await service.CreateSubscriptionAsync(productHandle, userId);

        var response = new CreateSubscriptionResponse
        {
            SubscriptionId = sub.id,
            State = sub.state,
            Price = sub.product_price_in_cents / 100m,
            PlanName = sub.product?.name ?? productHandle,
            PlanHandle = sub.product?.handle ?? productHandle,
            NextBillingDate = sub.current_period_ends_at ?? string.Empty
        };
        return Results.Ok(response);
    }
}
