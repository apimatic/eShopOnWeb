using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists subscriptions for the current shopper",
        Description = "Reads the shopper's current subscription state from Maxio.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    [ProducesResponseType(typeof(ListMySubscriptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptionBillingService.ListMySubscriptionsAsync(userName, cancellationToken);
            return Ok(new ListMySubscriptionsResponse { Subscriptions = subscriptions });
        }
        catch (SubscriptionBillingException exception)
        {
            return SubscriptionEndpointHelpers.FromException(this, exception);
        }
    }
}
