using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to the signed-in shopper.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SubscriptionPlanListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public SubscriptionPlanListEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists the subscription plans available in the configured Maxio product family",
        OperationId = "subscription-plans.list",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionBillingService.ListPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse
        {
            SubscriptionPlans = plans.Select(plan => new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Price = SubscriptionDto.FormatPrice(plan.PriceInCents),
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                PriceSummary = $"{SubscriptionDto.FormatPrice(plan.PriceInCents)} per {plan.Interval} {plan.IntervalUnit}{(plan.Interval == 1 ? "" : "s")}"
            }).ToList()
        };

        return response;
    }
}
