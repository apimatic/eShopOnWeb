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

public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionListRequest, IMaxioService>
{
    private readonly IHttpContextAccessor _accessor;
    public MySubscriptionListEndpoint(IHttpContextAccessor accessor) => _accessor = accessor;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxio) =>
            {
                return await HandleAsync(new MySubscriptionListRequest(), maxio);
            })
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionListRequest request, IMaxioService maxio)
    {
        var response = new MySubscriptionListResponse(request.CorrelationId());
        var userName = _accessor.HttpContext?.User.Identity?.Name ?? "unknown";
        var customerObj = await maxio.GetCustomerByReferenceAsync(userName);
        if (customerObj == null || customerObj["customer"] == null)
        {
            response.Subscriptions = new();
            return Results.Ok(response);
        }
        int customerId = customerObj["customer"]["id"]!.GetValue<int>();
        var subs = await maxio.GetSubscriptionsByCustomerAsync(customerId);
        response.Subscriptions = new();
        if (subs != null)
        {
            foreach (var s in subs)
            {
                if (s is JsonObject so && so["subscription"] is JsonObject subObj)
                {
                    response.Subscriptions.Add(new MySubscriptionDto
                    {
                        SubId = subObj["id"]?.GetValue<int>() ?? 0,
                        PlanHandle = subObj["product"]?["handle"]?.GetValue<string>() ?? subObj["product_id"]?.GetValue<string>() ?? "",
                        PlanId = subObj["product_id"]?.GetValue<int>() ?? 0,
                        State = subObj["state"]?.GetValue<string>() ?? "",
                        NextBillingDate = subObj["next_billing_at"]?.GetValue<string>() ?? "",
                        Price = subObj["product_price_in_cents"] != null ? subObj["product_price_in_cents"]!.GetValue<int>() / 100.0m : 0
                    });
                }
            }
        }
        return Results.Ok(response);
    }
}

public class MySubscriptionListResponse : BaseResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
    public MySubscriptionListResponse(Guid correlationId) : base(correlationId) { }
}

public class MySubscriptionDto
{
    public int SubId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public int PlanId { get; set; }
    public string State { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
