using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MinimalApi.Endpoint;
using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateRequest : BaseRequest
{
    public string PlanHandle { get; set; } = "eshop-pro";
}
public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }
    public bool Created { get; set; }
    public int? CustomerId { get; set; }
    public int? SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public decimal Price { get; set; }
}

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, Services.IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscriptionCreateEndpoint(IHttpContextAccessor accessor) { _accessor = accessor; }
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest req, Services.IMaxioSubscriptionService svc, IHttpContextAccessor accessor) =>
            {
                var endpoint = new SubscriptionCreateEndpoint(accessor);
                return await endpoint.HandleAsync(req, svc);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, Services.IMaxioSubscriptionService svc)
    {
        var ctx = _accessor.HttpContext;
        var email = ctx?.User?.Identity?.Name ?? ctx?.User?.FindFirst(ClaimTypes.Email)?.Value ?? "unknown@local";
        var userId = ctx?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? email;
        var handle = string.IsNullOrWhiteSpace(request.PlanHandle) ? "eshop-pro" : request.PlanHandle;

        var result = await svc.EnsureCustomerAndSubscribeAsync(email, userId, handle);
        var resp = new SubscriptionCreateResponse(request.CorrelationId())
        {
            Created = result.Created,
            CustomerId = result.CustomerId,
            SubscriptionId = result.SubscriptionId,
            PlanHandle = handle,
            State = result.Subscription?.State ?? "unknown",
            NextBillingAt = result.Subscription?.NextBillingAt,
            Price = result.Subscription?.PriceInCents ?? 0
        };
        return Results.Ok(resp);
    }
}
