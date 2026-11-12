using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class ListPlansResponse : BaseResponse
{
    public ListPlansResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public ListPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (BillingService billing) =>
            {
                return Results.Ok(new ListPlansResponse { Plans = await billing.ListPlansAsync() });
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

public class CreatePlanRequest
{
    public string? Name { get; set; }

    /// <summary>Monthly price, in major units (for example 19.99).</summary>
    public decimal? Price { get; set; }

    /// <summary>Alias of <see cref="Price"/>.</summary>
    public decimal? MonthlyPrice { get; set; }

    /// <summary>Id of the catalog brand the plan belongs to.</summary>
    public int? BrandId { get; set; }

    /// <summary>Name (or id) of the catalog brand, when <see cref="BrandId"/> is not given.</summary>
    public string? Brand { get; set; }
}

/// <summary>
/// Staff add a plan
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePlanRequest? request, BillingService billing) =>
            {
                if (request is null)
                {
                    throw new BillingException(400, "A request body with name, price and brand is required.");
                }
                var plan = await billing.CreatePlanAsync(request.Name, request.Price ?? request.MonthlyPrice, request.BrandId, request.Brand);
                return Results.Created($"api/plans#{plan.Id}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }
}

public class CurrencyPriceRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Staff price a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int planId, CurrencyPriceRequest? request, BillingService billing) =>
            {
                if (request is null)
                {
                    throw new BillingException(400, "A request body with currency and price is required.");
                }
                return Results.Ok(await billing.SetCurrencyPriceAsync(planId, request.Currency, request.Price));
            })
            .Produces<CurrencyPriceDto>()
            .WithTags("BillingEndpoints");
    }
}

public class NextBillingDateRequest
{
    /// <summary>The date (YYYY-MM-DD) the subscriber is next billed.</summary>
    public string? Date { get; set; }

    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// Staff set the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, NextBillingDateRequest? request, BillingService billing) =>
            {
                if (request is null)
                {
                    throw new BillingException(400, "A request body with the date is required.");
                }
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date ?? request.NextBillingDate));
            })
            .Produces<NextBillingDto>()
            .WithTags("BillingEndpoints");
    }
}
