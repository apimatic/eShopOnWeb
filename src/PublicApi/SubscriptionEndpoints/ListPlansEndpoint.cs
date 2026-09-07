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
public class ListPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlanResponse[]>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "List available subscription plans",
        Description = "Retrieves all available subscription plans from the billing system",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlanResponse[]>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptionService.GetSubscriptionPlansAsync(cancellationToken);
            return Ok(plans.Select(p => new SubscriptionPlanResponse
            {
                PlanId = p.PlanId,
                PlanName = p.PlanName,
                PlanHandle = p.PlanHandle,
                PriceInCents = p.PriceInCents,
                PriceFormatted = FormatPrice(p.PriceInCents),
                BillingInterval = p.BillingInterval,
                BillingPeriod = p.BillingPeriod,
                ExpiresNever = p.ExpiresNever
            }).ToArray());
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "Failed to retrieve subscription plans", detail = ex.Message });
        }
    }

    private static string FormatPrice(long priceInCents)
    {
        var dollars = priceInCents / 100m;
        return $"${dollars:F2}";
    }
}

public sealed class SubscriptionPlanResponse
{
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public int BillingInterval { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public bool ExpiresNever { get; set; }
}
