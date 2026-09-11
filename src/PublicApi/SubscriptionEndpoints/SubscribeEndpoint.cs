using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly IMaxioBillingService _billing;

    public SubscribeEndpoint(IMaxioBillingService billing) => _billing = billing;

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", Description = "Subscribe using JWT identity", OperationId = "subscription.subscribe", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var userId = User?.Identity?.Name ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        if (string.IsNullOrEmpty(userId) || userId == "anonymous")
            return Unauthorized();

        var sub = await _billing.SubscribeAsync(userId, request.PlanHandle ?? "eshop-pro", cancellationToken);
        return new SubscribeResponse(sub, userId);
    }
}

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse
{
    public SubscribeResponse(MaxioAdvancedBilling.Models.Subscription? subscription, string userId)
    {
        UserId = userId;
        SubscriptionId = subscription?.Id.HasValue == true ? (int)subscription.Id.Value : null;
        State = subscription?.State;
        ProductHandle = subscription?.Product?.Handle;
        CurrentPeriodEndsAt = subscription?.CurrentPeriodEndsAt;
    }
    public string UserId { get; set; }
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

}
