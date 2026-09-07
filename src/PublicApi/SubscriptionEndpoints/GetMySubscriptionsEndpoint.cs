using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly IMaxioApiService _maxioApiService;

    public GetMySubscriptionsEndpoint(
        ISubscriptionService subscriptionService,
        IMaxioApiService maxioApiService)
    {
        _subscriptionService = subscriptionService;
        _maxioApiService = maxioApiService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal principal) => await Handle(principal))
           .Produces<GetMySubscriptionsResponse>()
           .Produces(StatusCodes.Status401Unauthorized)
           .WithTags("SubscriptionEndpoints")
           .WithName("GetMySubscriptions")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync()
    {
        return Results.Unauthorized();
    }

    public async Task<IResult> Handle(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        var response = new GetMySubscriptionsResponse();

        var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId);

        foreach (var subscription in subscriptions)
        {
            var maxioSubscription = await _maxioApiService.GetSubscriptionAsync(subscription.MaxioSubscriptionId);

            response.Subscriptions.Add(new UserSubscriptionDto
            {
                SubscriptionId = subscription.MaxioSubscriptionId,
                ProductHandle = subscription.ProductHandle,
                State = subscription.State,
                PriceInCents = (long)subscription.PriceInCents,
                IntervalUnit = subscription.IntervalUnit,
                Interval = subscription.Interval,
                ActivatedAt = subscription.ActivatedAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ProductName = maxioSubscription?.Product?.Name ?? "",
                ProductId = maxioSubscription?.Product?.Id ?? 0
            });
        }

        return Results.Ok(response);
    }
}

public class UserSubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public int ProductId { get; set; }
    public string State { get; set; } = null!;
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public int Interval { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
}

public class GetMySubscriptionsResponse : BaseResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
