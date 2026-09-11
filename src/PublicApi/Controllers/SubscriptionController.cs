using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Controllers;

[ApiController]
[Authorize]
public class SubscriptionController : ControllerBase
{
    private readonly IMaxioService _svc;
    private readonly MaxioSettings _settings;

    public SubscriptionController(IMaxioService svc, IOptions<MaxioSettings> opts)
    {
        _svc = svc;
        _settings = opts.Value;
    }

    [HttpGet("api/subscription-plans")]
    public async Task<IActionResult> Plans()
    {
        var node = await _svc.GetProductsAsync(_settings.ProductFamilyHandle);
        if (node is JsonArray arr)
        {
            var filtered = arr.OfType<JsonObject>()
                .Select(j => j["product"] as JsonObject)
                .Where(p => p != null)
                .Where(p => (p["product_family"] as JsonObject)?["handle"]?.GetValue<string>() == _settings.ProductFamilyHandle)
                .Select(p => new
                {
                    id = p["id"]?.GetValue<int>(),
                    handle = p["handle"]?.GetValue<string>(),
                    name = p["name"]?.GetValue<string>(),
                    price_in_cents = p["price_in_cents"]?.GetValue<int>(),
                    interval = p["interval"]?.GetValue<int>(),
                    interval_unit = p["interval_unit"]?.GetValue<string>(),
                    description = p["description"]?.GetValue<string>(),
                    require_credit_card = p["require_credit_card"]?.GetValue<bool>()
                })
                .ToList();
            return Ok(filtered);
        }
        return Ok(new List<object>());
    }

    [HttpPost("api/subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] JsonObject? body)
    {
        var user = User;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity?.Name ?? "unknown";
        var userEmail = user.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@example.com";
        var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
        var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? "Name";
        var planHandle = body?["planHandle"]?.GetValue<string>() ?? body?["productHandle"]?.GetValue<string>() ?? "eshop-pro";

        var customerNode = await _svc.GetCustomerByReferenceAsync(userId);
        int customerId = 0;
        if (customerNode == null)
        {
            var created = await _svc.CreateCustomerAsync(userId, firstName, lastName, userEmail);
            customerId = created?["customer"]?["id"]?.GetValue<int>() ?? 0;
        }
        else
        {
            customerId = customerNode["customer"]?["id"]?.GetValue<int>() ?? 0;
        }

        var subsNode = await _svc.GetCustomerSubscriptionsAsync(customerId);
        JsonArray? subsArr = subsNode is JsonObject subsObj ? subsObj["subscriptions"] as JsonArray : subsNode as JsonArray;
        if (subsArr != null)
        {
            foreach (var item in subsArr.OfType<JsonObject>())
            {
                var sub = item["subscription"] as JsonObject ?? item;
                if (sub?["product_handle"]?.GetValue<string>() == planHandle)
                {
                    return Ok(new { id = sub["id"]?.GetValue<int>(), state = sub["state"]?.GetValue<string>(), product_handle = sub["product_handle"]?.GetValue<string>(), product_name = sub["product_name"]?.GetValue<string>(), product_price_in_cents = sub["product_price_in_cents"]?.GetValue<int>(), current_period_ends_at = sub["current_period_ends_at"]?.GetValue<string>(), next_assessment_at = sub["next_assessment_at"]?.GetValue<string>(), message = "Subscription already exists" });
                }
            }
        }
        var subNode = await _svc.CreateSubscriptionAsync(planHandle, userId);
        var subObj = subNode?["subscription"] as JsonObject ?? subNode as JsonObject;
        return Ok(new { id = subObj?["id"]?.GetValue<int>(), state = subObj?["state"]?.GetValue<string>(), product_handle = subObj?["product_handle"]?.GetValue<string>(), product_name = subObj?["product_name"]?.GetValue<string>(), product_price_in_cents = subObj?["product_price_in_cents"]?.GetValue<int>(), current_period_ends_at = subObj?["current_period_ends_at"]?.GetValue<string>(), next_assessment_at = subObj?["next_assessment_at"]?.GetValue<string>(), message = "Subscription created" });
    }

    [HttpGet("api/my-subscriptions")]
    public async Task<IActionResult> MySubscriptions()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.Identity?.Name ?? "unknown";
        var customerNode = await _svc.GetCustomerByReferenceAsync(userId);
        if (customerNode == null) return Ok(new List<object>());
        var customerId = customerNode["customer"]?["id"]?.GetValue<int>() ?? 0;
        if (customerId == 0) return Ok(new List<object>());
        var subsNode = await _svc.GetCustomerSubscriptionsAsync(customerId);
        JsonArray? subsArr = subsNode is JsonObject subsObj ? subsObj["subscriptions"] as JsonArray : subsNode as JsonArray;
        var result = new List<object>();
        if (subsArr != null)
        {
            foreach (var item in subsArr.OfType<JsonObject>())
            {
                var sub = item["subscription"] as JsonObject ?? item;
                result.Add(new { id = sub["id"]?.GetValue<int>(), state = sub["state"]?.GetValue<string>(), product_handle = sub["product_handle"]?.GetValue<string>(), product_name = sub["product_name"]?.GetValue<string>(), product_price_in_cents = sub["product_price_in_cents"]?.GetValue<int>(), current_period_ends_at = sub["current_period_ends_at"]?.GetValue<string>(), next_assessment_at = sub["next_assessment_at"]?.GetValue<string>(), created_at = sub["created_at"]?.GetValue<string>() });
            }
        }
        return Ok(result);
    }
}
