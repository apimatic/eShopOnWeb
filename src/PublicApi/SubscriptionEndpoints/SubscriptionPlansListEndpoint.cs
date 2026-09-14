using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available for the signed-in shopper.
/// </summary>
public class SubscriptionPlansListEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<SubscriptionPlansListResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<SubscriptionPlansListEndpoint> _logger;

    public SubscriptionPlansListEndpoint(ISubscriptionService subscriptionService, ILogger<SubscriptionPlansListEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [Authorize]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the subscription plans available to subscribe to",
        Description = "Lists the subscription plans in the configured Maxio product family, including price and billing interval.",
        OperationId = "subscriptions.plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(SubscriptionPlansListResponse), StatusCodes.Status200OK)]
    public override async Task<ActionResult<SubscriptionPlansListResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new SubscriptionPlansListResponse();

        try
        {
            MaxioProductFamily? family = await _subscriptionService.GetProductFamilyAsync(cancellationToken);
            IReadOnlyList<MaxioProduct> plans = await _subscriptionService.ListPlansAsync(cancellationToken);

            response.ProductFamilyName = family?.Name;
            response.ProductFamilyHandle = family?.Handle;

            foreach (MaxioProduct plan in plans)
            {
                response.Plans.Add(plan.ToPlanDto(family));
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return SubscriptionErrorResponses.FromException(ex, _logger);
        }
    }
}
