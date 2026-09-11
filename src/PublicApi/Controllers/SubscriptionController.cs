using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Controllers;

[ApiController]
[Route("api/")]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly IMaxioClient _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionController(IMaxioClient maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    [HttpGet("subscription-plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _maxio.GetPlansAsync();
        return Ok(plans.Select(p => new
        {
            p.Id,
            p.Name,
            p.Handle,
            Price = p.PriceInCents / 100.0,
            Currency = "USD",
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        }));
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.ProductHandle))
            return BadRequest(new { error = "product_handle is required" });

        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
            return NotFound(new { error = "User not found" });

        var reference = user.Id ?? username;

        // Idempotent customer lookup
        var existing = await _maxio.FindCustomerByReferenceAsync(reference);
        if (existing == null)
        {
            // Create customer implicitly via subscription creation with customer_attributes
        }

        var email = user.Email ?? $"{username}@example.com";
        var firstName = username;
        var lastName = "User";

        try
        {
            var result = await _maxio.CreateSubscriptionAsync(
                reference,
                req.ProductHandle,
                email,
                firstName,
                lastName);

            if (result == null)
                return StatusCode(500, new { error = "Maxio did not return subscription" });

            return Ok(new
            {
                subscriptionId = result.Id,
                state = result.State,
                productHandle = result.ProductHandle,
                productName = result.ProductName,
                nextBillingAt = result.NextBillingAt
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { error = "Billing service error", details = ex.Message });
        }
    }

    [HttpGet("my-subscriptions")]
    public async Task<IActionResult> MySubscriptions()
    {
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(username);
        if (user == null)
            return NotFound(new { error = "User not found" });

        var reference = user.Id ?? username;
        var customer = await _maxio.FindCustomerByReferenceAsync(reference);
        if (customer == null)
            return Ok(new List<object>());

        var subs = await _maxio.GetCustomerSubscriptionsAsync(customer.CustomerId);
        return Ok(subs.Select(s => new
        {
            s.Id,
            s.State,
            s.ProductHandle,
            s.ProductName,
            s.NextBillingAt
        }));
    }
}

public class SubscribeRequest
{
    public string ProductHandle { get; set; } = "";
}
