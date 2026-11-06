using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminBillingEndpoints;

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }

    /// <summary>Name of one of the catalog brands.</summary>
    public string? Brand { get; set; }
    public int? BrandId { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    public DateOnly? Date { get; set; }
    public DateOnly? NextBillingDate { get; set; }
}

/// <summary>Staff add a plan.</summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, BillingService billing) =>
                await HandleAsync(request, billing))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePlanRequest request, BillingService billing) =>
        BillingResults.RunAsync(async () =>
        {
            var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand, request.BrandId, default);
            return Results.Created($"api/plans/{plan.Handle}", plan);
        });
}

/// <summary>Staff price a plan in another currency.</summary>
public class CreateCurrencyPriceEndpoint : IEndpoint<IResult, long, CurrencyPriceRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:long}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long planId, CurrencyPriceRequest request, BillingService billing) =>
                await HandleAsync(planId, request, billing))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(long planId, CurrencyPriceRequest request, BillingService billing) =>
        BillingResults.RunAsync(async () =>
        {
            var created = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, default);
            return Results.Created($"api/admin/plans/{planId}/currency-prices", created);
        });
}

/// <summary>Staff set the date a subscriber is next billed.</summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, long, NextBillingDateRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:long}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, NextBillingDateRequest request, BillingService billing) =>
                await HandleAsync(subscriptionId, request, billing))
            .Produces<SubscriptionDto>()
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(long subscriptionId, NextBillingDateRequest request, BillingService billing) =>
        BillingResults.RunAsync(async () =>
            Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date ?? request.NextBillingDate, default)));
}
