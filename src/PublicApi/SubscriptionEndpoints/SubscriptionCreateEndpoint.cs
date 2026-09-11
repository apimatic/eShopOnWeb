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
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient, IMaxioCustomerService, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _http;
    public SubscriptionCreateEndpoint(IHttpContextAccessor http) { _http = http; }
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req, MaxioAdvancedBillingClient c, IMaxioCustomerService s, UserManager<ApplicationUser> um) => await HandleAsync(req,c,s,um))
           .Produces<CreateSubscriptionResponse>().WithTags("SubscriptionEndpoints").RequireAuthorization();
    }
    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient c, IMaxioCustomerService s, UserManager<ApplicationUser> um)
    { return Results.BadRequest(new { error = "POST via lambda" }); }
    public async Task<IResult> HandleAsync(CreateSubscriptionRequest req, MaxioAdvancedBillingClient c, IMaxioCustomerService s, UserManager<ApplicationUser> um)
    {
        try
        {
            var uid = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if(string.IsNullOrEmpty(uid)) return Results.Unauthorized();
            var user = await um.FindByIdAsync(uid);
            if(user==null) return Results.NotFound();
            var cid = await s.EnsureCustomerAsync(uid, user.Email??$"{uid}@localhost", "", "");
            var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest { Subscription = new MaxioAdvancedBilling.Models.CreateSubscription { ProductHandle = req.PlanHandle ?? "eshop-pro", CustomerId = cid } };
            var result = await c.Subscriptions.CreateSubscription(body, ct: default);
            var sub = result.Subscription;
            return Results.Ok(new CreateSubscriptionResponse { SubscriptionId = sub?.Id??0, ProductHandle = req.PlanHandle??"eshop-pro", CustomerId = cid, State = sub?.State??"active", NextBillingDate = sub?.CurrentPeriodEndsAt??DateTimeOffset.UtcNow.AddMonths(1) });
        }
        catch(Exception ex) { return Results.Problem(ex.Message, statusCode: 500); }
    }
}
public class CreateSubscriptionRequest { public string PlanHandle { get; set; } = "eshop-pro"; }
public class CreateSubscriptionResponse { public int SubscriptionId{get;set;} public string ProductHandle{get;set;}=""; public int CustomerId{get;set;} public string State{get;set;}=""; public DateTimeOffset NextBillingDate{get;set;} }
