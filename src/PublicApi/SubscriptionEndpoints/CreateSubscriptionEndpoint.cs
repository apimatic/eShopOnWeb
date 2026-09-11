using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string ProductHandle { get; set; } = "eshop-pro";
}

public class SubscribeResponse
{
    public int CustomerId { get; set; }
    public int? SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public string Message { get; set; } = "";
}

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly SubscriptionService _svc;
    public CreateSubscriptionEndpoint(SubscriptionService svc) => _svc = svc;

    [Authorize]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", Tags = new[] { "Subscription" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var identity = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.Identity?.Name ?? "unknown";
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? $"{identity}@eshop.local";

        var userSub = await _svc.SubscribeAsync(identity, email, request.ProductHandle);

        string state = "active";
        DateTime? nextBilling = null;

        return Ok(new SubscribeResponse
        {
            CustomerId = userSub.MaxioCustomerId,
            SubscriptionId = userSub.MaxioSubscriptionId,
            ProductHandle = userSub.ProductHandle,
            State = state,
            NextBillingDate = nextBilling ?? DateTime.UtcNow.AddMonths(1),
            Message = $"Subscribed to {request.ProductHandle} (customer {userSub.MaxioCustomerId})"
        });
    }
}

