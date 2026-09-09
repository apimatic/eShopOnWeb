using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for purchase.
/// </summary>
[Authorize]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlanListResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionPlanListEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the subscription plans available for purchase",
        OperationId = "subscription.plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscriptionPlanListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlanListResponse(Guid.NewGuid());
        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        response.Plans = plans.Select(ToDto).ToList();
        return response;
    }

    internal static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = FormatPrice(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    internal static SubscriptionSummaryDto ToDto(SubscriptionSummary summary) => new()
    {
        MaxioSubscriptionId = summary.MaxioSubscriptionId,
        MaxioCustomerId = summary.MaxioCustomerId,
        ProductHandle = summary.ProductHandle,
        ProductName = summary.ProductName,
        PriceInCents = summary.PriceInCents,
        Price = FormatPrice(summary.PriceInCents),
        State = summary.State,
        ActivatedAt = summary.ActivatedAt,
        NextBillingDate = summary.NextBillingDate
    };

    private static string FormatPrice(int priceInCents) =>
        (priceInCents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
