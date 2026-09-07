using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class ListUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListUserSubscriptionsResponse>
{
    private readonly MaxioAdvancedBillingClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListUserSubscriptionsEndpoint(MaxioAdvancedBillingClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Retrieves all active subscriptions for the logged-in user",
        OperationId = "subscriptions.list_user",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListUserSubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "User identity not found" });
        }

        var response = new ListUserSubscriptionsResponse();

        try
        {
            var subscriptions = await _maxioClient.Subscriptions.ListSubscriptions(
                state: null,
                product: null,
                productPricePointId: null,
                coupon: null,
                couponCode: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                metadata: null,
                direction: null,
                sort: null,
                include: null,
                page: 1,
                perPage: 100,
                ct: cancellationToken);

            foreach (var sub in subscriptions)
            {
                if (sub.Subscription == null)
                    continue;

                if (sub.Subscription.Customer?.Reference != userId)
                    continue;

                response.Subscriptions.Add(new UserSubscriptionItem
                {
                    Id = sub.Subscription.Id ?? 0,
                    State = sub.Subscription.State?.ToString() ?? "Unknown",
                    ProductName = sub.Subscription.Product?.Name ?? string.Empty,
                    ProductPriceInCents = sub.Subscription.ProductPriceInCents ?? 0,
                    NextBillingDate = sub.Subscription.NextAssessmentAt,
                    ActivatedAt = sub.Subscription.ActivatedAt,
                    CurrentPeriodEndsAt = sub.Subscription.CurrentPeriodEndsAt
                });
            }

            return response;
        }
        catch (SdkException<RawError> ex)
        {
            return StatusCode((int?)ex.Error.StatusCode ?? 500, new { error = "Failed to retrieve subscriptions" });
        }
        catch (System.Text.Json.JsonException ex)
        {
            return StatusCode(500, new { error = "Failed to process subscriptions", details = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "An unexpected error occurred", details = ex.Message });
        }
    }
}

public class UserSubscriptionItem
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

public class ListUserSubscriptionsResponse
{
    public List<UserSubscriptionItem> Subscriptions { get; set; } = new();
}
