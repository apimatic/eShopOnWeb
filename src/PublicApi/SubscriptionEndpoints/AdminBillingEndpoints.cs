using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreatePlanRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }
    /// <summary>Name of a brand in the shop's catalog</summary>
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequest
{
    /// <summary>YYYY-MM-DD</summary>
    public string? Date { get; set; }
}

/// <summary>Staff: adds a plan</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePlanRequest request, BillingService billing) =>
                Results.Created("api/plans", await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand)))
            .WithTags(Shopper.TAG);
    }
}

/// <summary>Staff: prices a plan in another currency</summary>
public class AddCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:long}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long planId, CurrencyPriceRequest request, BillingService billing) =>
                Results.Created($"api/admin/plans/{planId}/currency-prices", await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price)))
            .WithTags(Shopper.TAG);
    }
}

/// <summary>Staff: sets the date a subscriber is next billed</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:long}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (long subscriptionId, NextBillingDateRequest request, BillingService billing) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date)))
            .WithTags(Shopper.TAG);
    }
}
