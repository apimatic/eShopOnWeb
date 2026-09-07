using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.IdentityModel.JsonWebTokens;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<SubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Subscribe the authenticated user to a subscription plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var userClaims = HttpContext.User;
            var userId = userClaims.FindFirst("sub")?.Value ?? userClaims.FindFirst("nameid")?.Value;
            var userEmail = userClaims.FindFirst("email")?.Value;
            var firstName = userClaims.FindFirst("given_name")?.Value ?? "User";
            var lastName = userClaims.FindFirst("family_name")?.Value ?? "Account";

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userEmail))
            {
                return Unauthorized(new { error = "User identity not found in token" });
            }

            if (string.IsNullOrEmpty(request.PlanHandle))
            {
                return BadRequest(new { error = "Plan handle is required" });
            }

            var subscription = await _subscriptionService.SubscribeAsync(
                userId, userEmail, firstName, lastName, request.PlanHandle, cancellationToken);

            return CreatedAtAction(nameof(HandleAsync), new SubscriptionResponse
            {
                SubscriptionId = subscription.SubscriptionId,
                State = subscription.State,
                PriceInCents = subscription.PriceInCents,
                PriceFormatted = FormatPrice(subscription.PriceInCents),
                NextBillingDate = subscription.NextBillingDate,
                ActiveSince = subscription.ActiveSince,
                CanceledAt = subscription.CanceledAt,
                ExpiresAt = subscription.ExpiresAt,
                CreatedAt = subscription.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to create subscription", detail = ex.Message });
        }
    }

    private static string FormatPrice(long priceInCents)
    {
        var dollars = priceInCents / 100m;
        return $"${dollars:F2}";
    }
}

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActiveSince { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}
