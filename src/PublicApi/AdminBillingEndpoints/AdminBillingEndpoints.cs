using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminBillingEndpoints;

public class CreatePlanRequest
{
    public string? Name { get; set; }

    /// <summary>Monthly price.</summary>
    public decimal? Price { get; set; }

    /// <summary>Alias of <see cref="Price"/>.</summary>
    public decimal? MonthlyPrice { get; set; }

    /// <summary>The catalog brand's name.</summary>
    public string? Brand { get; set; }

    /// <summary>Alternatively, the catalog brand's id.</summary>
    public int? BrandId { get; set; }
}

/// <summary>
/// Staff: adds a plan with a name, monthly price and catalog brand
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, HttpContext http, MaxioBillingService billing, OperationClaimStore claims, IRepository<CatalogBrand> brands) =>
                await HandleAsync(request, http, billing, claims, brands))
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePlanRequest request, HttpContext http, MaxioBillingService billing,
        OperationClaimStore claims, IRepository<CatalogBrand> brands) =>
        BillingEndpointHelper.RunAsync(http, async ct =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return BillingEndpointHelper.Failure(400, "A plan name is required.");
            var price = request.Price ?? request.MonthlyPrice;
            if (price is null || price <= 0 || price > 10_000_000m)
                return BillingEndpointHelper.Failure(400, "A monthly price greater than zero is required.");
            if (decimal.Round(price.Value, 2) != price.Value)
                return BillingEndpointHelper.Failure(400, "The price cannot have more than two decimal places.");

            // The brand must be one of the shop's catalog brands.
            var all = await brands.ListAsync(ct);
            CatalogBrand? brand = null;
            if (request.BrandId is not null)
                brand = all.FirstOrDefault(b => b.Id == request.BrandId);
            else if (!string.IsNullOrWhiteSpace(request.Brand))
                brand = all.FirstOrDefault(b => string.Equals(b.Brand, request.Brand.Trim(), StringComparison.OrdinalIgnoreCase));
            if (brand is null)
                return BillingEndpointHelper.Failure(400, "The brand must be one of the brands in the catalog.");

            var claimKey = $"create-plan:{billing.PlanHandleFor(name, brand.Id)}";
            if (!await claims.TryClaimAsync(claimKey, ct))
                return BillingEndpointHelper.Failure(409, "This plan is already being created.");
            try
            {
                var plans = await billing.ListPlansAsync(ct);
                var handle = billing.PlanHandleFor(name, brand.Id);
                if (plans.Plans.Any(p => p.Handle == handle))
                    return BillingEndpointHelper.Failure(409, "A plan with this name already exists for this brand.");

                var plan = await billing.CreatePlanAsync(name, price.Value, brand.Id, brand.Brand, ct);
                return Results.Created($"api/plans", new PlanDto
                {
                    Id = plan.Id,
                    Handle = plan.Handle,
                    Name = plan.Name,
                    Description = plan.Description,
                    PriceInCents = plan.PriceInCents,
                    MonthlyPrice = plan.PriceInCents / 100m,
                    Interval = plan.Interval,
                    IntervalUnit = plan.IntervalUnit,
                    Brand = plan.Brand
                });
            }
            finally
            {
                await claims.ReleaseAsync(claimKey);
            }
        });
}

public class CurrencyPriceRequest
{
    /// <summary>ISO currency code, e.g. EUR.</summary>
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

/// <summary>
/// Staff: prices a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, HttpContext http, MaxioBillingService billing, OperationClaimStore claims) =>
                await HandleAsync(planId, request, http, billing, claims))
            .Produces<MaxioBillingService.CurrencyPriceInfo>(StatusCodes.Status201Created)
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(int planId, CurrencyPriceRequest request, HttpContext http,
        MaxioBillingService billing, OperationClaimStore claims) =>
        BillingEndpointHelper.RunAsync(http, async ct =>
        {
            var currency = request.Currency?.Trim().ToUpperInvariant();
            if (currency is null || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
                return BillingEndpointHelper.Failure(400, "A three-letter currency code is required.");
            if (request.Price is null || request.Price <= 0)
                return BillingEndpointHelper.Failure(400, "A price greater than zero is required.");
            if (request.Price != decimal.Truncate(request.Price.Value) || request.Price > int.MaxValue)
                return BillingEndpointHelper.Failure(400, "The price must be a whole number of currency units.");

            var claimKey = $"currency-price:{planId}:{currency}";
            if (!await claims.TryClaimAsync(claimKey, ct))
                return BillingEndpointHelper.Failure(409, "This currency price is already being set.");
            try
            {
                var made = await billing.AddCurrencyPriceAsync(planId, currency, (int)request.Price.Value, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices", made);
            }
            finally
            {
                await claims.ReleaseAsync(claimKey);
            }
        });
}

public class NextBillingDateRequest
{
    /// <summary>The date, as YYYY-MM-DD.</summary>
    public string? Date { get; set; }

    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// Staff: sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, HttpContext http, MaxioBillingService billing) =>
                await HandleAsync(subscriptionId, request, http, billing))
            .Produces<SubscriptionDto>()
            .WithTags("AdminBillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, NextBillingDateRequest request, HttpContext http, MaxioBillingService billing) =>
        BillingEndpointHelper.RunAsync(http, async ct =>
        {
            var text = string.IsNullOrWhiteSpace(request.Date) ? request.NextBillingDate : request.Date;
            if (!BillingEndpointHelper.TryParseDate(text, out var date))
                return BillingEndpointHelper.Failure(400, "A date is required, as YYYY-MM-DD.");
            if (date <= DateOnly.FromDateTime(DateTime.UtcNow))
                return BillingEndpointHelper.Failure(400, "The next billing date must be in the future.");
            return Results.Ok(SubscriptionDto.From(await billing.SetNextBillingDateAsync(subscriptionId, date, ct)));
        });
}
