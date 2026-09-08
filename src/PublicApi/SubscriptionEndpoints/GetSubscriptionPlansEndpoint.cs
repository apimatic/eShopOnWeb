using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available in the configured Maxio product family
/// </summary>
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly MaxioOptions _maxioOptions;

    public GetSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> maxioOptions)
    {
        _subscriptionService = subscriptionService;
        _maxioOptions = maxioOptions.Value;
    }

    [HttpGet("api/subscription-plans")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists the subscription plans available in the configured Maxio product family",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans = plans
            .Select(p => new SubscriptionPlanDto
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                BillingInterval = p.Interval,
                BillingIntervalUnit = p.IntervalUnit,
                IsDefault = IsDefaultPlan(p, plans)
            })
            .ToList();

        return Ok(response);
    }

    private bool IsDefaultPlan(MaxioProduct plan, System.Collections.Generic.IReadOnlyList<MaxioProduct> plans)
    {
        if (!string.IsNullOrWhiteSpace(_maxioOptions.DefaultProductHandle))
        {
            return plan.Handle == _maxioOptions.DefaultProductHandle;
        }

        var cheapest = plans.OrderBy(p => p.PriceInCents).First();
        return plan.Handle == cheapest.Handle;
    }
}
