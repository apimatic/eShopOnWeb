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

public class MySubscriptionResponse
{
    public int CustomerId { get; set; }
    public int? SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? NextBillingDate { get; set; }
    public decimal PriceMonthly { get; set; }
}

public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<MySubscriptionResponse>>
{
    private readonly SubscriptionService _svc;
    private readonly MaxioClient _maxio;
    public MySubscriptionsEndpoint(SubscriptionService svc, MaxioClient maxio)
    {
        _svc = svc;
        _maxio = maxio;
    }

    [Authorize]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Get my subscriptions", Tags = new[] { "Subscription" })]
    public override async Task<ActionResult<List<MySubscriptionResponse>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var identity = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.Identity?.Name ?? "unknown";
        var userSub = await _svc.GetAsync(identity);
        if (userSub == null || userSub.MaxioSubscriptionId == null)
            return Ok(new List<MySubscriptionResponse>());

        var results = new List<MySubscriptionResponse>
        {
            new()
            {
                CustomerId = userSub.MaxioCustomerId,
                SubscriptionId = userSub.MaxioSubscriptionId,
                ProductHandle = userSub.ProductHandle,
                State = "active",
                NextBillingDate = DateTime.UtcNow.AddMonths(1),
                PriceMonthly = userSub.ProductHandle == "eshop-pro" ? 299m : (userSub.ProductHandle == "basic-plan" ? 29m : 0m)
            }
        };
        return Ok(results);
    }
}

