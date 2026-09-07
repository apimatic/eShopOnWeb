using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioApiClient _maxioClient;
    private readonly AppIdentityDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(
        MaxioApiClient maxioClient,
        AppIdentityDbContext dbContext,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/my-subscriptions", Handle)
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();

    private async Task<IResult> Handle()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Results.Unauthorized();
            }

            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var mapping = await _dbContext.MaxioCustomerMappings
                .FirstOrDefaultAsync(m => m.UserId == userId);

            if (mapping == null)
            {
                return Results.Ok(new GetMySubscriptionsResponse { Subscriptions = new List<UserSubscription>() });
            }

            var subscriptionsResponse = await _maxioClient.GetCustomerSubscriptionsAsync(mapping.MaxioCustomerId);
            if (subscriptionsResponse == null)
            {
                return Results.Ok(new GetMySubscriptionsResponse { Subscriptions = new List<UserSubscription>() });
            }

            var subscriptions = subscriptionsResponse
                .Where(sr => sr.Subscription != null)
                .Select(sr => new UserSubscription
                {
                    SubscriptionId = sr.Subscription!.Id,
                    State = sr.Subscription.State,
                    ProductName = sr.Subscription.Product?.Name ?? "",
                    ProductHandle = sr.Subscription.Product?.Handle ?? "",
                    PriceInCents = sr.Subscription.Product?.PriceInCents ?? 0,
                    CurrentPeriodEndsAt = sr.Subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = sr.Subscription.NextAssessmentAt,
                    CreatedAt = sr.Subscription.CreatedAt
                })
                .ToList();

            return Results.Ok(new GetMySubscriptionsResponse { Subscriptions = subscriptions });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsResponse
{
    public List<UserSubscription> Subscriptions { get; set; } = new();
}

public class UserSubscription
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
