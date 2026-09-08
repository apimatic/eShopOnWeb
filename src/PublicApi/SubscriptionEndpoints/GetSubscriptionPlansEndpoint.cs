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
public class GetSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<GetSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists the recurring subscription plans available to subscribe to",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<GetSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new GetSubscriptionPlansResponse(Guid.NewGuid());

        var plans = await _subscriptionService.GetPlansAsync(cancellationToken);
        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            Price = p.PriceInCents / 100m,
            BillingInterval = p.BillingInterval,
            BillingIntervalUnit = p.BillingIntervalUnit,
            RequiresPaymentMethod = p.RequiresPaymentMethod
        }).ToList();

        return Ok(response);
    }
}
