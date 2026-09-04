using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionBillingService _billing;

    public SubscriptionsController(ISubscriptionBillingService billing)
    {
        _billing = billing;
    }

    [HttpGet("subscription-plans")]
    public async Task<ActionResult<SubscriptionPlansResponse>> GetPlans()
    {
        var plans = await _billing.GetPlansAsync(HttpContext.RequestAborted);
        return Ok(new SubscriptionPlansResponse { Plans = plans });
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<SubscriptionResponse>> CreateSubscription([FromBody] SubscribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return BadRequest(new { message = "PlanHandle is required." });

        var subscription = await _billing.SubscribeAsync(User, request.PlanHandle, HttpContext.RequestAborted);
        return Ok(subscription);
    }

    [HttpGet("my-subscriptions")]
    public async Task<ActionResult<MySubscriptionsResponse>> GetMySubscriptions()
    {
        var subscriptions = await _billing.GetMySubscriptionsAsync(User, HttpContext.RequestAborted);
        return Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
    }
}
