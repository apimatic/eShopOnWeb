using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.eShopWeb.PublicApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService svc, HttpContext ctx) => await HandleAsync(new MySubscriptionsRequest { UserName = ctx.User.FindFirst(ClaimTypes.Name)?.Value ?? ctx.User.Identity?.Name ?? "unknown" }, svc))
            .Produces<List<MySubscriptionResponse>>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService svc)
    {
        var subs = await svc.GetMySubscriptionsAsync(request.UserName);
        return Results.Ok(subs.Select(s => new MySubscriptionResponse
        {
            SubscriptionId = s.Id,
            PlanHandle = s.ProductHandle,
            State = s.State,
            CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
            NextBillingAt = s.NextBillingAt,
            CustomerId = s.CustomerId
        }).ToList());
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    public string UserName { get; set; } = "";
}

public class MySubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public int CustomerId { get; set; }
}
