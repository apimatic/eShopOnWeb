using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public GetMySubscriptionsEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns the list of subscriptions for the authenticated user",
        OperationId = "subscriptions.getMySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var subscriptions = await _subscriptionService.GetUserSubscriptionsAsync(userId);

        var response = new GetMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
            {
                Id = s.Id,
                MaxioSubscriptionId = s.MaxioSubscriptionId,
                MaxioCustomerId = s.MaxioCustomerId,
                ProductHandle = s.ProductHandle,
                State = s.State,
                CurrentPrice = s.CurrentPrice,
                NextBillingAt = s.NextBillingAt
            }).ToList()
        };

        return Ok(response);
    }
}

public class GetMySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public DateTime NextBillingAt { get; set; }
}
