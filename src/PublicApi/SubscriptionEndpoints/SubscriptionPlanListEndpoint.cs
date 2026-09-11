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

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, MaxioAdvancedBillingClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (MaxioAdvancedBillingClient client) => await HandleAsync(client))
           .Produces<ListSubscriptionPlansResponse>().WithTags("SubscriptionEndpoints").RequireAuthorization();
    }
    public async Task<IResult> HandleAsync(MaxioAdvancedBillingClient client)
    {
        try { var list = await client.Products.ListProducts(dateField: null, filter: null, endDate: null, endDatetime: null, startDate: null, startDatetime: null, includeArchived: null, include: null, page: 1, perPage: 10, ct: default); var resp = new ListSubscriptionPlansResponse(); foreach(var item in list) { var p = item.Product; if(p!=null) resp.Plans.Add(new SubscriptionPlanDto{ Handle=p.Id?.ToString()??"", Name=p.Name??"", Price=p.PriceInCents.HasValue?p.PriceInCents.Value/100m:0, Id=p.Id??0 }); } return Results.Ok(resp); }
        catch { return Results.Problem("Maxio error", statusCode: 502); }
    }
}
public class ListSubscriptionPlansResponse { public List<SubscriptionPlanDto> Plans { get; } = new(); }
public class SubscriptionPlanDto { public string Handle{get;set;}=""; public string Name{get;set;}=""; public decimal Price{get;set;} public string Interval{get;set;}="month"; public int Id{get;set;} }
