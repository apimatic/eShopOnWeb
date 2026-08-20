using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

[Authorize]
public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly SubscriptionEndpointUserResolver _userResolver;
    private readonly SubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(SubscriptionEndpointUserResolver userResolver,
        SubscriptionService subscriptionService)
    {
        _userResolver = userResolver;
        _subscriptionService = subscriptionService;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Creates a subscription",
        Description = "Idempotently subscribes the authenticated user to a Maxio plan.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userResolver.ResolveAsync(User);
        if (user is null)
            return Unauthorized();

        var subscription = await _subscriptionService.SubscribeAsync(user, request.ProductHandle.Trim(),
            cancellationToken);
        return Ok(new CreateSubscriptionResponse(request.CorrelationId()) { Subscription = subscription });
    }
}
