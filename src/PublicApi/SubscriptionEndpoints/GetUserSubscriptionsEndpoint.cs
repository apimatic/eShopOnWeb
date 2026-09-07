using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<UserSubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetUserSubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Retrieves all subscriptions for the authenticated user",
        OperationId = "subscriptions.getUser",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<UserSubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var userClaims = HttpContext.User;
            var userId = userClaims.FindFirst("sub")?.Value ?? userClaims.FindFirst("nameid")?.Value;
            var userEmail = userClaims.FindFirst("email")?.Value;

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
            {
                return Unauthorized(new { error = "User identity not found in token" });
            }

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId, userEmail, cancellationToken);

            return Ok(new UserSubscriptionsResponse
            {
                UserId = userId,
                Subscriptions = subscriptions.Select(s => new SubscriptionResponse
                {
                    SubscriptionId = s.SubscriptionId,
                    State = s.State,
                    PriceInCents = s.PriceInCents,
                    PriceFormatted = FormatPrice(s.PriceInCents),
                    NextBillingDate = s.NextBillingDate,
                    ActiveSince = s.ActiveSince,
                    CanceledAt = s.CanceledAt,
                    ExpiresAt = s.ExpiresAt,
                    CreatedAt = s.CreatedAt
                }).ToArray()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve subscriptions", detail = ex.Message });
        }
    }

    private static string FormatPrice(long priceInCents)
    {
        var dollars = priceInCents / 100m;
        return $"${dollars:F2}";
    }
}

public sealed class UserSubscriptionsResponse
{
    public string UserId { get; set; } = string.Empty;
    public SubscriptionResponse[] Subscriptions { get; set; } = Array.Empty<SubscriptionResponse>();
}
