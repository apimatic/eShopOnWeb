using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, SubscriptionService>
{
    private HttpContext? _httpContext;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (SubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var endpoint = new GetMySubscriptionsEndpoint { _httpContext = httpContext };
                return await endpoint.HandleAsync(new EmptyRequest(), subscriptionService);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, SubscriptionService subscriptionService)
    {
        var userId = _httpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "demouser";

        var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(userId);

        return Results.Ok(new GetMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new MySubscriptionDto
            {
                Id = s.Id,
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                PlanHandle = s.PlanHandle,
                PlanName = s.PlanName,
                PriceInDollars = s.PriceInDollars,
                State = s.State,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                CreatedAt = s.CreatedAt
            }).ToList()
        });
    }
}

public class GetMySubscriptionsResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
