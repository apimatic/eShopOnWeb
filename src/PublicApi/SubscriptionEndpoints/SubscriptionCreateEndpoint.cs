using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest req, IMaxioBillingService service) =>
            {
                return await HandleAsync(req, service);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioBillingService service)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var reference = user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.Identity?.Name ?? "anonymous";

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
            return Results.BadRequest(new { error = "ProductHandle required" });

        var sub = await service.SubscribeAsync(reference, request.ProductHandle);
        if (sub == null)
            return Results.BadRequest(new { error = "Subscription failed" });

        var response = new SubscriptionCreateResponse(request.CorrelationId())
        {
            Subscription = new SubscriptionDto
            {
                Id = sub.Id,
                CustomerReference = sub.CustomerReference,
                ProductHandle = sub.ProductHandle,
                State = sub.State,
                NextBillingDate = sub.NextBillingDate,
                Price = sub.Price
            }
        };
        return Results.Ok(response);
    }
}
