using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the billing system of record.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the subscription plans available in the billing system of record",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse
        {
            CorrelationId = Guid.NewGuid().ToString()
        };

        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            PlanHandle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Price = FormatPrice(p.PriceInCents),
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));

        return Ok(response);
    }

    private static string FormatPrice(long priceInCents) =>
        $"${priceInCents / 100m:0.00}";
}
