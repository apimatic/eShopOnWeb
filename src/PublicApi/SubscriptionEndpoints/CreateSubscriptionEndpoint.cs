using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscriptionDto>
{
    private readonly ISubscriptionBillingService _billingService;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated shopper to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscriptionDto>> HandleAsync(
        SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _billingService.SubscribeAsync(
            User,
            request.ProductHandle,
            cancellationToken);
        return result.Created
            ? StatusCode(201, result.Subscription)
            : Ok(result.Subscription);
    }
}
