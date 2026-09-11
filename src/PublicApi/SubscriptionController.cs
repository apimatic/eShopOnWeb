using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Controllers;

[Route("api/subscription-plans")]
[ApiController]
[Authorize]
public class SubscriptionPlansController : ControllerBase
{
    private readonly IMaxioBillingService _service;
    public SubscriptionPlansController(IMaxioBillingService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<SubscriptionPlanDto>>> Get() => Ok(await _service.GetPlansAsync());
}

[Route("api/subscriptions")]
[ApiController]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly IMaxioBillingService _service;
    public SubscriptionsController(IMaxioBillingService service) => _service = service;

    [HttpPost]
    public async Task<ActionResult<SubscriptionResultDto>> Post([FromBody] SubscriptionCreateRequest req)
    {
        var userRef = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "unknown";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? userRef;
        return Ok(await _service.SubscribeAsync(userRef, email, req.ProductHandle));
    }
}

public class SubscriptionCreateRequest { public string ProductHandle { get; set; } = string.Empty; }

[Route("api/my-subscriptions")]
[ApiController]
[Authorize]
public class MySubscriptionsController : ControllerBase
{
    private readonly IMaxioBillingService _service;
    public MySubscriptionsController(IMaxioBillingService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<MySubscriptionDto>>> Get()
    {
        var userRef = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "unknown";
        return Ok(await _service.GetMySubscriptionsAsync(userRef));
    }
}
