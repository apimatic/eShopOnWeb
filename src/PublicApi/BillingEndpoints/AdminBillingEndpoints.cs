using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>Staff add a plan (name, monthly price, catalog brand).</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePlanRequest request, MaxioBillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlanAsync(request.Name, request.Price, request.Brand, ct);
                return Results.Created($"api/plans/{plan.Id}", plan);
            })
            .Produces<PlanDto>(201)
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff price a plan in another currency.</summary>
public class SetPlanCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int planId, CurrencyPriceRequest request, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetCurrencyPriceAsync(planId, request.Currency, request.Price, ct)))
            .Produces<CurrencyPriceDto>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>Staff set the date a subscriber is next billed.</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int subscriptionId, NextBillingDateRequest request, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date, ct)))
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }
}
