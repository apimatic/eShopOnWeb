using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan. Idempotent: a shopper who is already on the requested
/// plan gets the existing subscription back instead of a second one.
/// </summary>
public class SubscribeEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly IMaxioBillingService _subscriptionService;

    public SubscribeEndpoint(IMaxioBillingService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a plan",
        Description = "Subscribes the current shopper to the given plan. Repeated requests for the same plan return the existing subscription.",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var shopperReference = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(shopperReference))
        {
            return Unauthorized();
        }

        var outcome = await _subscriptionService.SubscribeAsync(
            shopperReference,
            shopperReference,
            request.PlanHandle.Trim(),
            cancellationToken);

        var site = await _subscriptionService.GetSiteAsync(cancellationToken);
        var currency = site.Currency ?? "USD";

        var response = new SubscribeResponse
        {
            Subscription = SubscriptionDtoMapper.ToSubscriptionDto(outcome.Subscription, currency),
            Created = outcome.Created,
            Message = outcome.Created
                ? $"Subscribed to {outcome.Subscription.Product?.Name ?? request.PlanHandle}."
                : $"You are already subscribed to {outcome.Subscription.Product?.Name ?? request.PlanHandle}."
        };

        if (outcome.Created)
        {
            return Created($"api/subscriptions/{outcome.Subscription.Id}", response);
        }

        return response;
    }
}
