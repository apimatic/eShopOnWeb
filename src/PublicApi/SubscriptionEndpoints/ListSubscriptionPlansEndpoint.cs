using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans (Maxio products in the configured product family) a shopper can
/// subscribe to.
/// </summary>
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<ListSubscriptionPlansEndpoint> _logger;

    public ListSubscriptionPlansEndpoint(ISubscriptionService subscriptionService,
        ILogger<ListSubscriptionPlansEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists available subscription plans",
        Description = "Lists the subscription plans a shopper can subscribe to",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(ListSubscriptionPlansResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var plans = await _subscriptionService.GetAvailablePlansAsync(cancellationToken);
            var response = new ListSubscriptionPlansResponse
            {
                SubscriptionPlans = plans.ToList()
            };
            return Ok(response);
        }
        catch (Exception ex) when (ex is MaxioConfigurationException or MaxioApiException)
        {
            return SubscriptionEndpointErrorMapper.ToObjectResult(ex, _logger);
        }
    }
}
