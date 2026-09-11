using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Enrollment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
}

public class SubscriptionListEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly IHttpContextAccessor _http;

    public SubscriptionListEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxio) =>
            {
                return await HandleAsync(maxio);
            })
           .Produces<List<MySubscriptionDto>>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(IMaxioService maxio)
    {
        var user = _http.HttpContext?.User;
        var userName = user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.Identity?.Name ?? string.Empty;
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();

        var response = await maxio.ListSubscriptionsByCustomerReferenceAsync(userName);
        if (response == null) return Results.Ok(new List<MySubscriptionDto>());

        var list = new List<MySubscriptionDto>();
        if (response["subscriptions"] is System.Text.Json.Nodes.JsonArray subs)
        {
            foreach (var s in subs)
            {
                if (s is null) continue;
                list.Add(new MySubscriptionDto
                {
                    Id = s["id"]?.GetValue<int>() ?? 0,
                    ProductHandle = s["product"]?["handle"]?.GetValue<string>() ?? string.Empty,
                    State = s["state"]?.GetValue<string>() ?? string.Empty,
                    PriceInCents = s["product_price_in_cents"]?.GetValue<int>() ?? 0,
                    CurrentPeriodEndsAt = s["current_period_ends_at"]?.GetValue<string>() ?? string.Empty
                });
            }
        }
        return Results.Ok(list);
    }
}
