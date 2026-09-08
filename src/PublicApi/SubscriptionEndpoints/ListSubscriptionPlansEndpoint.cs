using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscription;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers (from the billing system of record).
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public ListSubscriptionPlansEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists the recurring subscription plans available to shoppers",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await _subscriptionBillingService.ListPlansAsync(cancellationToken);
        response.SubscriptionPlans.AddRange(plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequiresPaymentMethod = p.RequiresPaymentMethod,
            ProductFamilyHandle = p.ProductFamilyHandle
        }));

        return Ok(response);
    }
}
