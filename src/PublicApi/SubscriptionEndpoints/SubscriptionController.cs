using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly ICurrentBillingUserService _currentUserService;

    public SubscriptionController(
        ISubscriptionBillingService billingService,
        ICurrentBillingUserService currentUserService)
    {
        _billingService = billingService;
        _currentUserService = currentUserService;
    }

    [HttpGet("subscription-plans")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionPlanDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlanDto>>> ListPlans(CancellationToken cancellationToken)
    {
        _ = await _currentUserService.GetAsync(cancellationToken);
        return Ok(await _billingService.ListPlansAsync(cancellationToken));
    }

    [HttpPost("subscriptions")]
    [ProducesResponseType(typeof(SubscriptionDto), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(SubscriptionDto), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(SubscriptionPendingDto), (int)HttpStatusCode.Accepted)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    public async Task<ActionResult> Subscribe(SubscribeRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "A valid productHandle is required.");
        }

        var user = await _currentUserService.GetAsync(cancellationToken);
        var result = await _billingService.SubscribeAsync(user, request.ProductHandle, cancellationToken);
        if (result.IsUnknown)
        {
            return Accepted(
                "/api/my-subscriptions",
                new SubscriptionPendingDto(
                    request.ProductHandle,
                    "unknown",
                    "The request was sent to Maxio but is not yet confirmed. No duplicate create will be attempted."));
        }

        return result.Created
            ? Created("/api/my-subscriptions", result.Subscription)
            : Ok(result.Subscription);
    }

    [HttpGet("my-subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionDto>>> ListMine(CancellationToken cancellationToken)
    {
        var user = await _currentUserService.GetAsync(cancellationToken);
        return Ok(await _billingService.ListForUserAsync(user, cancellationToken));
    }
}
