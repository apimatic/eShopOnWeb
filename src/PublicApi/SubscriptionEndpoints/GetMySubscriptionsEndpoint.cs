using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsRequest : BaseRequest { }

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public string PriceDisplay => $"${PriceInCents / 100.0:F2}";
    public string? CurrentPeriodEndsAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? NextAssessmentAt { get; set; }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse() { }
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpCtx;
    public GetMySubscriptionsEndpoint(IHttpContextAccessor httpCtx) { _httpCtx = httpCtx; }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioBillingService svc) =>
            {
                return await HandleAsync(new GetMySubscriptionsRequest(), svc);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioBillingService svc)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());
        var ctx = _httpCtx.HttpContext;
        var userId = ctx?.User?.Identity?.Name ?? ctx?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var customer = await svc.FindOrCreateCustomerAsync(userId, "", "", "");
        var subs = await svc.GetCustomerSubscriptionsAsync(customer.Id.ToString());
        response.Subscriptions = subs.Select(s => new MySubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.ProductHandle ?? "",
            PriceInCents = s.PriceInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            ActivatedAt = s.ActivatedAt,
            NextAssessmentAt = s.NextAssessmentAt
        }).ToList();
        return Results.Ok(response);
    }
}
