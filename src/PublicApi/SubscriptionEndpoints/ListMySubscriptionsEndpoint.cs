using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, MaxioService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor, ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (MaxioService maxioService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), maxioService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, MaxioService maxioService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var customerId = await maxioService.GetCustomerIdByReferenceAsync(userId, default);
            var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customerId, default);

            response.Subscriptions = subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => new SubscriptionDto
                {
                    Id = s.Subscription!.Id ?? 0,
                    State = s.Subscription.State?.Value ?? "unknown",
                    ProductHandle = s.Subscription.Product?.Handle ?? string.Empty,
                    ProductName = s.Subscription.Product?.Name ?? string.Empty,
                    ProductPriceInCents = s.Subscription.ProductPriceInCents ?? 0,
                    CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt = s.Subscription.NextAssessmentAt,
                    ActivatedAt = s.Subscription.ActivatedAt,
                    CreatedAt = s.Subscription.CreatedAt
                })
                .ToList();

            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Subscriptions = new List<SubscriptionDto>();
            return Results.Ok(response);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error listing subscriptions for user {UserId}: {Status}", userId, ex.Error.StatusCode);
            response.ErrorMessage = $"Billing service error: {(int)ex.Error.StatusCode}";
            return Results.StatusCode(502);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing subscriptions for user {UserId}", userId);
            response.ErrorMessage = "An unexpected error occurred.";
            return Results.StatusCode(500);
        }
    }
}
