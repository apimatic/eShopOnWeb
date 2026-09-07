using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionService>
{
    private HttpContext? _httpContext;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, SubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                var endpoint = new CreateSubscriptionEndpoint { _httpContext = httpContext };
                return await endpoint.HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "Plan handle is required" });
        }

        var userId = _httpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "demouser";
        var userEmail = _httpContext?.User?.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@eshop.local";
        var userName = _httpContext?.User?.FindFirst(ClaimTypes.Name)?.Value ?? userId;
        var names = userName.Split(' ', 2);
        var firstName = names[0];
        var lastName = names.Length > 1 ? names[1] : "User";

        var subscription = await subscriptionService.CreateSubscriptionAsync(
            userId,
            request.PlanHandle,
            userEmail,
            firstName,
            lastName);

        if (subscription == null)
        {
            return Results.BadRequest(new { error = "Failed to create subscription" });
        }

        return Results.Ok(new CreateSubscriptionResponse
        {
            Id = subscription.Id,
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInDollars = subscription.PriceInDollars,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt
        });
    }
}

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
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
