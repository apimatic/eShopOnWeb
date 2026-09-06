using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get subscriptions for the current user
/// </summary>
public class GetMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingService _billingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(
        IMaxioBillingService billingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingService = billingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleAsync)
            .RequireAuthorization()
            .Produces<GetMySubscriptionsResponse>()
            .Produces(StatusCodes.Status500InternalServerError)
            .WithTags("SubscriptionEndpoints")
            .WithName("GetMySubscriptions");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
            {
                return Results.Unauthorized();
            }

            // Get current user ID
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            // Get subscriptions
            var subscriptions = await _billingService.GetUserSubscriptionsAsync(userId);

            var response = new GetMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(s => new SubscriptionInfoResponse
                {
                    Id = s.Id,
                    ProductHandle = s.ProductHandle,
                    State = s.State,
                    NextBillingAt = s.NextBillingAt,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class SubscriptionInfoResponse
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class GetMySubscriptionsResponse
{
    public List<SubscriptionInfoResponse> Subscriptions { get; set; } = new();
}
