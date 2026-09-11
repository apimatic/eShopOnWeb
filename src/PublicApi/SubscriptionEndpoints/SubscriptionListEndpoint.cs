using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListRequest : BaseRequest { }
public class SubscriptionListResponse : BaseResponse
{
    public SubscriptionListResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
public class SubscriptionDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public decimal Price { get; set; }
}

public class SubscriptionListEndpoint : IEndpoint<IResult, SubscriptionListRequest, Services.IMaxioSubscriptionService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscriptionListEndpoint(IHttpContextAccessor accessor) { _accessor = accessor; }
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (Services.IMaxioSubscriptionService svc, IHttpContextAccessor accessor) =>
            {
                var endpoint = new SubscriptionListEndpoint(accessor);
                return await endpoint.HandleAsync(new SubscriptionListRequest(), svc);
            })
            .Produces<SubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionListRequest request, Services.IMaxioSubscriptionService svc)
    {
        var ctx = _accessor.HttpContext;
        var email = ctx?.User?.Identity?.Name ?? ctx?.User?.FindFirst(ClaimTypes.Email)?.Value ?? "unknown@local";
        var subs = await svc.ListMySubscriptionsAsync(email);
        var resp = new SubscriptionListResponse(request.CorrelationId());
        resp.Subscriptions = subs.Select(s => new SubscriptionDto
        {
            PlanHandle = s.Handle,
            State = s.State,
            NextBillingAt = s.NextBillingAt,
            Price = s.PriceInCents
        }).ToList();
        return Results.Ok(resp);
    }
}
