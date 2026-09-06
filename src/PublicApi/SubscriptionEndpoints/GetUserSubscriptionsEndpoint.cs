using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class GetUserSubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetUserSubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public GetUserSubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Retrieves all active subscriptions for the authenticated user",
        OperationId = "subscriptions.list",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<GetUserSubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(
                userId,
                cancellationToken);

            var response = new GetUserSubscriptionsResponse
            {
                Subscriptions = subscriptions
                    .Select(s => new UserSubscriptionDto
                    {
                        Id = s.Id,
                        State = s.State,
                        ProductHandle = s.ProductHandle,
                        PricePointHandle = s.PricePointHandle,
                        CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                        NextAssessmentAt = s.NextAssessmentAt
                    })
                    .ToArray()
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed class GetUserSubscriptionsResponse
{
    public UserSubscriptionDto[] Subscriptions { get; set; } = Array.Empty<UserSubscriptionDto>();
}

public sealed class UserSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string PricePointHandle { get; set; } = "";
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
}
