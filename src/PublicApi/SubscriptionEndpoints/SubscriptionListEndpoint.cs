using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionListEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient, IMaxioCustomerService>
{
    private readonly IHttpContextAccessor _http;
    public SubscriptionListEndpoint(IHttpContextAccessor http){_http=http;}
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (MaxioAdvancedBillingClient c, IMaxioCustomerService s)=>await HandleAsync(c,s))
           .Produces<ListMySubscriptionsResponse>().WithTags("SubscriptionEndpoints").RequireAuthorization();
    }
    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient c, IMaxioCustomerService s)
    {
        try
        {
            var uid = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(string.IsNullOrEmpty(uid)) return Results.Unauthorized();
            var cid = await s.FindCustomerIdAsync(uid);
            if(cid==null) return Results.Ok(new ListMySubscriptionsResponse());
            var subs = await c.Customers.ListCustomerSubscriptions(cid.Value, ct: default);
            var resp = new ListMySubscriptionsResponse();
            foreach(var item in subs) { var sub = item.Subscription; if(sub!=null) resp.Subscriptions.Add(new MySubscriptionDto{Id=sub.Id??0, ProductHandle="", State=sub.State??"", NextBillingDate=sub.CurrentPeriodEndsAt??DateTimeOffset.MinValue}); }
            return Results.Ok(resp);
        }
        catch { return Results.Problem("Maxio error", statusCode:502); }
    }
}
public class ListMySubscriptionsResponse { public List<MySubscriptionDto> Subscriptions { get; } = new(); }
public class MySubscriptionDto { public int Id{get;set;} public string ProductHandle{get;set;}=""; public string State{get;set;}=""; public DateTimeOffset NextBillingDate{get;set;} }
