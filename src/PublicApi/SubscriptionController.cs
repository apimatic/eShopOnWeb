using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Services;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.PublicApi;

[ApiController]
[Authorize(AuthenticationSchemes = "Bearer")]
public class SubscriptionsController : ControllerBase
{
    private readonly IMaxioService _maxio;

    public SubscriptionsController(IMaxioService maxio)
    {
        _maxio = maxio;
    }

    private string UserReference => User.Identity?.Name ?? "anonymous";

    [HttpGet("/api/subscription-plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _maxio.GetSubscriptionPlansAsync();
        var result = new List<object>();
        foreach (var node in plans)
        {
            if (node is JsonObject obj)
            {
                result.Add(new
                {
                    handle = obj["handle"]?.GetValue<string>(),
                    name = obj["name"]?.GetValue<string>(),
                    price_in_cents = obj["price_in_cents"]?.GetValue<int?>(),
                    interval = obj["interval"]?.GetValue<int?>(),
                    interval_unit = obj["interval_unit"]?.GetValue<string>(),
                    product_family_handle = obj["product_family"]?["handle"]?.GetValue<string>(),
                    require_credit_card = obj["require_credit_card"]?.GetValue<bool?>()
                });
            }
        }
        return Ok(new { plans = result });
    }

    [HttpPost("/api/subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
            return BadRequest(new { error = "product_handle is required" });

        var reference = UserReference;
        // Idempotent customer creation
        var customer = await _maxio.FindCustomerByReferenceAsync(reference);
        if (customer == null)
        {
            var user = User.Identity?.Name ?? "User";
            customer = await _maxio.CreateCustomerAsync(reference, user, user, $"{reference}@localhost");
        }

        // Idempotent subscription: check existing
        var existing = await _maxio.GetSubscriptionsByCustomerReferenceAsync(reference);
        foreach (var sub in existing)
        {
            if (sub is JsonObject s && s["product"]?["handle"]?.GetValue<string>() == request.ProductHandle)
            {
                return Ok(new
                {
                    subscription = new
                    {
                        id = s["id"]?.GetValue<int?>(),
                        state = s["state"]?.GetValue<string>(),
                        product_handle = s["product"]?["handle"]?.GetValue<string>(),
                        product_price_in_cents = s["product_price_in_cents"]?.GetValue<int?>(),
                        current_period_ends_at = s["current_period_ends_at"]?.GetValue<string>(),
                        customer_reference = s["customer"]?["reference"]?.GetValue<string>()
                    },
                    message = "Subscription already exists"
                });
            }
        }

        var newSub = await _maxio.CreateSubscriptionAsync(reference, request.ProductHandle);
        return Ok(new
        {
            subscription = new
            {
                id = newSub["id"]?.GetValue<int?>(),
                state = newSub["state"]?.GetValue<string>(),
                product_handle = newSub["product"]?["handle"]?.GetValue<string>(),
                product_price_in_cents = newSub["product_price_in_cents"]?.GetValue<int?>(),
                current_period_ends_at = newSub["current_period_ends_at"]?.GetValue<string>(),
                customer_reference = newSub["customer"]?["reference"]?.GetValue<string>(),
                activated_at = newSub["activated_at"]?.GetValue<string>(),
                created_at = newSub["created_at"]?.GetValue<string>()
            }
        });
    }

    [HttpGet("/api/my-subscriptions")]
    public async Task<IActionResult> MySubscriptions()
    {
        var reference = UserReference;
        var subs = await _maxio.GetSubscriptionsByCustomerReferenceAsync(reference);
        var list = new List<object>();
        foreach (var sub in subs)
        {
            if (sub is JsonObject s)
            {
                list.Add(new
                {
                    id = s["id"]?.GetValue<int?>(),
                    state = s["state"]?.GetValue<string>(),
                    product_handle = s["product"]?["handle"]?.GetValue<string>(),
                    product_price_in_cents = s["product_price_in_cents"]?.GetValue<int?>(),
                    current_period_ends_at = s["current_period_ends_at"]?.GetValue<string>(),
                    activated_at = s["activated_at"]?.GetValue<string>(),
                    created_at = s["created_at"]?.GetValue<string>(),
                    customer_reference = s["customer"]?["reference"]?.GetValue<string>()
                });
            }
        }
        return Ok(new { subscriptions = list });
    }
}

public class SubscribeRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
