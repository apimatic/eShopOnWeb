using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly IMaxioBillingService _service;

    public SubscriptionController(IMaxioBillingService service) => _service = service;

    [HttpGet("subscription-plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _service.GetPlansAsync();
        return Ok(new { plans });
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "anon";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? userId + "@example.com";
        var result = await _service.SubscribeAsync(userId, email, req.PlanHandle ?? "eshop-pro");
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("my-subscriptions")]
    public async Task<IActionResult> MySubscriptions()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "anon";
        var subs = await _service.GetMySubscriptionsAsync(userId);
        return Ok(new { subscriptions = subs });
    }
}

public class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}
