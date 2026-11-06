using System;
using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreatePlanRequestBody
{
    public string? Name { get; set; }

    /// <summary>Monthly price in the site's currency.</summary>
    public decimal? Price { get; set; }

    /// <summary>Alias of <see cref="Price"/>.</summary>
    public decimal? MonthlyPrice { get; set; }

    /// <summary>Id of one of the shop's catalog brands.</summary>
    public int? BrandId { get; set; }

    /// <summary>Alias of <see cref="BrandId"/>.</summary>
    public int? CatalogBrandId { get; set; }

    /// <summary>The brand's name (or id as text), when the id is not given.</summary>
    public string? Brand { get; set; }
}

public class CurrencyPriceRequestBody
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequestBody
{
    /// <summary>YYYY-MM-DD.</summary>
    public string? Date { get; set; }

    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }
}

/// <summary>Staff adds a plan for one of the catalog's brands.</summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequestBody request, BillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlanAsync(request.Name, request.Price ?? request.MonthlyPrice,
                    request.BrandId ?? request.CatalogBrandId, request.Brand, ct);
                return Results.Created($"api/plans/{plan.Id}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingAdminEndpoints");
    }
}

/// <summary>Staff prices a plan in another currency.</summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequestBody request, BillingService billing, CancellationToken ct) =>
            {
                var result = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices/{result.Currency}", result);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("BillingAdminEndpoints");
    }
}

/// <summary>Staff sets the date a subscriber is next billed.</summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequestBody request, BillingService billing, CancellationToken ct) =>
            {
                var text = (request.Date ?? request.NextBillingDate)?.Trim();
                DateOnly? date = null;
                if (text is { Length: >= 10 } &&
                    DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    date = parsed;
                else if (!string.IsNullOrEmpty(text))
                    throw new BillingException(400, "The date must be formatted YYYY-MM-DD.");
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date, ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingAdminEndpoints");
    }
}
