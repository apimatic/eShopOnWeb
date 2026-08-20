using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

[Authorize]
public sealed class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly SubscriptionEndpointUserResolver _userResolver;
    private readonly SubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(SubscriptionEndpointUserResolver userResolver,
        SubscriptionService subscriptionService)
    {
        _userResolver = userResolver;
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists my subscriptions",
        Description = "Lists current Maxio subscriptions for the authenticated user.",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await _userResolver.ResolveAsync(User);
        if (user is null)
            return Unauthorized();

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(user, cancellationToken);
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);
        return Ok(response);
    }
}
