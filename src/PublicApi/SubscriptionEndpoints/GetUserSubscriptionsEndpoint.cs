using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUserSubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IRepository<Subscription> subscriptionRepository, SubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleAsync(subscriptionRepository, subscriptionService, httpContext);
            })
            .Produces<GetUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions")
            .RequireAuthorization();
    }

    private async Task<IResult> HandleAsync(IRepository<Subscription> subscriptionRepository, SubscriptionService subscriptionService, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst("sub")?.Value ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var userSubscriptions = await subscriptionRepository.ListAsync(new GetUserSubscriptionsSpecification(userId));

            var response = new GetUserSubscriptionsResponse
            {
                Subscriptions = userSubscriptions.Select(s => new UserSubscriptionResponse
                {
                    SubscriptionId = s.MaxioSubscriptionId,
                    ProductHandle = s.ProductHandle,
                    ProductName = s.ProductName,
                    PriceInCents = s.PriceInCents,
                    State = s.State,
                    ActivatedAt = s.ActivatedAt,
                    NextAssessmentAt = s.NextAssessmentAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetUserSubscriptionsResponse
{
    public List<UserSubscriptionResponse> Subscriptions { get; set; } = new();
}

public class UserSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long PriceInCents { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class GetUserSubscriptionsSpecification : Specification<Subscription>
{
    public GetUserSubscriptionsSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}
