using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the Maxio subscriptions belonging to the signed-in shopper.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(ISubscriptionService subscriptionService, ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the caller's subscriptions",
        Description = "Lists every Maxio subscription recorded for the signed-in shopper.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new ListMySubscriptionsResponse();

        var shopper = BillingShopperFactory.FromPrincipal(User);
        if (shopper == null)
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptionService.GetSubscriptionsAsync(shopper, cancellationToken);
            response.Subscriptions.AddRange(subscriptions);
            return Ok(response);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio request failed while listing subscriptions for {Reference}.", shopper.Reference);
            return StatusCode(502, new { message = "The billing provider could not be reached." });
        }
    }
}
