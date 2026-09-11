using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest, IMaxioService>
{
    private readonly IHttpContextAccessor _accessor;
    public SubscriptionCreateEndpoint(IHttpContextAccessor accessor) => _accessor = accessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (SubscriptionCreateRequest req, IMaxioService maxio) =>
            {
                return await HandleAsync(req, maxio);
            })
            .Produces<SubscriptionCreateResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request, IMaxioService maxio)
    {
        var response = new SubscriptionCreateResponse(request.CorrelationId());
        var userName = _accessor.HttpContext?.User.Identity?.Name ?? "unknown";
        var reference = userName;

        // Idempotent customer lookup / create
        var customerObj = await maxio.GetCustomerByReferenceAsync(reference);
        int customerId = 0;
        if (customerObj != null && customerObj["customer"]?["id"] != null)
        {
            customerId = customerObj["customer"]["id"]!.GetValue<int>();
        }
        else
        {
            var created = await maxio.CreateCustomerAsync(reference, request.Email ?? $"{reference}@local", request.FirstName, request.LastName);
            if (created == null || created["customer"] == null) return Results.Problem("Failed to create Maxio customer");
            customerId = created["customer"]["id"]!.GetValue<int>();
        }

        // Use planHandle if given, else planId
        int productId = request.PlanId > 0 ? request.PlanId : 0;
        if (productId == 0 && !string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            // Try to resolve handle to id via products; for now rely on caller sending id
            // If handle only, we'll try to find from family via products endpoint
            var prods = await maxio.GetProductsAsync();
            if (prods != null)
            {
                foreach (var p in prods)
                {
                    if (p is JsonObject obj && obj["handle"]?.GetValue<string>() == request.PlanHandle)
                    {
                        productId = obj["id"]!.GetValue<int>();
                        break;
                    }
                }
            }
        }

        if (productId == 0) return Results.BadRequest(new { error = "Plan id or handle required" });

        // Idempotency: check existing subscription for this customer + product
        var existingSubs = await maxio.GetSubscriptionsByCustomerAsync(customerId);
        if (existingSubs != null)
        {
            foreach (var s in existingSubs)
            {
                if (s is JsonObject so && so["subscription"]?["product_id"]?.GetValue<int>() == productId)
                {
                    // Return existing
                    response.SubId = so["subscription"]["id"]?.GetValue<int>() ?? 0;
                    response.PlanHandle = request.PlanHandle ?? "";
                    response.PlanId = productId;
                    response.State = so["subscription"]["state"]?.GetValue<string>() ?? "";
                    response.NextBillingDate = so["subscription"]["next_billing_at"]?.GetValue<string>() ?? "";
                    response.CustomerReference = reference;
                    response.Status = "existing";
                    return Results.Ok(response);
                }
            }
        }

        var sub = await maxio.CreateSubscriptionAsync(customerId, productId, reference);
        if (sub == null || sub["subscription"] == null) return Results.Problem("Failed to create Maxio subscription");

        response.SubId = sub["subscription"]["id"]?.GetValue<int>() ?? 0;
        response.PlanId = sub["subscription"]["product_id"]?.GetValue<int>() ?? productId;
        response.PlanHandle = request.PlanHandle ?? "";
        response.State = sub["subscription"]["state"]?.GetValue<string>() ?? "";
        response.NextBillingDate = sub["subscription"]["next_billing_at"]?.GetValue<string>() ?? sub["subscription"]["current_period_started_at"]?.GetValue<string>() ?? "";
        response.CustomerReference = reference;
        response.Status = "created";
        response.Price = sub["subscription"]["product_price_in_cents"] != null ? sub["subscription"]["product_price_in_cents"]!.GetValue<int>() / 100.0m : 0;
        return Results.Ok(response);
    }
}

public class SubscriptionCreateRequest : BaseRequest
{
    public int PlanId { get; set; }
    public string? PlanHandle { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class SubscriptionCreateResponse : BaseResponse
{
    public int SubId { get; set; }
    public int PlanId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // created / existing
    public decimal Price { get; set; }
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }
}
