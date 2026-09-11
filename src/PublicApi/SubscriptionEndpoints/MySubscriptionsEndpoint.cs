using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioBillingService _billing;

    public MySubscriptionsEndpoint(IMaxioBillingService billing) => _billing = billing;

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "My subscriptions", Description = "List current user's Maxio subscriptions", OperationId = "subscription.mySubscriptions", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var userId = User?.Identity?.Name ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        if (string.IsNullOrEmpty(userId) || userId == "anonymous")
            return Unauthorized();

        var subs = await _billing.GetMySubscriptionsAsync(userId, cancellationToken);
        return new MySubscriptionsResponse(subs, userId);
    }
}

public class MySubscriptionsResponse
{
    public MySubscriptionsResponse(IEnumerable<MaxioAdvancedBilling.Models.Subscription> subscriptions, string userId)
    {
        UserId = userId;
        Subscriptions = subscriptions.Select(s => new SubscriptionSummary(s)).ToList();
    }
    public string UserId { get; set; }
    public List<SubscriptionSummary> Subscriptions { get; set; }
}

public class SubscriptionSummary
{
    public SubscriptionSummary(MaxioAdvancedBilling.Models.Subscription s)
    {
        Id = s.Id.HasValue == true ? (int)s.Id.Value : null;
        State = s.State;
        ProductHandle = s.Product?.Handle;
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt;
    }
    public int? Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
