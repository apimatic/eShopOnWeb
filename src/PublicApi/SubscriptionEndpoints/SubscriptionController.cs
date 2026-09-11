using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.MaxioService;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[ApiController]
[Route("api")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly IMaxioBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionController(IMaxioBillingService billing, UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    [HttpGet("subscription-plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _billing.GetPlansAsync();
        return Ok(new { plans = plans.Select(p => new { p.Id, p.Handle, p.Name, p.Price, p.PriceFormatted }) });
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return BadRequest(new { error = "PlanHandle is required" });

        var userName = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        var reference = user?.Email ?? userName;

        var result = await _billing.SubscribeAsync(reference, request.PlanHandle);
        if (!result.Success)
            return StatusCode(502, new { success = false, error = result.Error });

        return Ok(new SubscribeResponse
        {
            Success = true,
            Message = "Subscribed successfully",
            SubscriptionId = result.SubscriptionId,
            State = result.State,
            NextBillingAt = result.NextBillingAt,
            PlanHandle = result.PlanHandle,
            Price = result.Price
        });
    }

    [HttpGet("my-subscriptions")]
    public async Task<IActionResult> MySubscriptions()
    {
        var userName = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        var reference = user?.Email ?? userName;

        var subs = await _billing.GetMySubscriptionsAsync(reference);
        return Ok(new { subscriptions = subs });
    }
}
