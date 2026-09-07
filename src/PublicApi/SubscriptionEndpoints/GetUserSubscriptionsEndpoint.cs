using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetUserSubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get current user's subscriptions",
        Description = "Returns the current user's active subscriptions",
        OperationId = "subscription.user",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value
                ?? throw new Exception("User ID not found in claims");

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, cancellationToken);

            return new GetUserSubscriptionsResponse
            {
                Subscriptions = subscriptions.ConvertAll(s => new UserSubscriptionResponse
                {
                    SubscriptionId = s.Id,
                    ProductHandle = s.ProductHandle,
                    State = s.State,
                    BalanceInCents = s.BalanceInCents,
                    CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
                })
            };
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
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
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
