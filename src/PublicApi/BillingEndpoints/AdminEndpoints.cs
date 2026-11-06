using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.ProductPricePoints;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Adds a plan (a Maxio product) that belongs to one of the catalog's brands
/// </summary>
public class CreatePlanEndpoint : IEndpoint<IResult, CreatePlanRequest, BillingService, IReadRepository<CatalogBrand>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, BillingService billing, IReadRepository<CatalogBrand> brands) =>
            {
                return await HandleAsync(request, billing, brands);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePlanRequest request, BillingService billing, IReadRepository<CatalogBrand> brands)
    {
        return BillingException.RunAsync(async () =>
        {
            var name = request?.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                throw BillingException.BadRequest("A plan name is required.");

            var price = request!.Price ?? request.MonthlyPrice;
            if (price == null || price < 0)
                throw BillingException.BadRequest("A monthly price of zero or more is required.");
            if (decimal.Round(price.Value, 2) != price.Value)
                throw BillingException.BadRequest("The monthly price can have at most two decimal places.");

            // The brand has to be one of the shop's catalog brands.
            var catalogBrands = await brands.ListAsync();
            var brand = request.BrandId != null
                ? catalogBrands.FirstOrDefault(b => b.Id == request.BrandId)
                : catalogBrands.FirstOrDefault(b => string.Equals(b.Brand, request.Brand?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (brand == null)
                throw BillingException.BadRequest("The brand must be one of the catalog brands: " + string.Join(", ", catalogBrands.Select(b => b.Brand)) + ".");

            var family = await EnsureFamilyAsync(billing, brand.Brand);

            var created = await billing.Client.Products.CreateProduct(new CreateProductRequest
            {
                ProductFamilyId = family.Id!.Value.ToString(CultureInfo.InvariantCulture),
                Body = new CreateOrUpdateProductRequest
                {
                    Product = new CreateOrUpdateProduct
                    {
                        Name = name,
                        Description = name,
                        PriceInCents = (long)decimal.Round(price.Value * 100m),
                        Interval = 1,
                        IntervalUnit = IntervalUnit.Month,
                        RequireCreditCard = false,
                    },
                },
            });

            var dto = BillingService.ToDto(created.Product);
            return Results.Created($"api/plans/{dto.Id}", dto);
        });
    }

    /// <summary>Each brand has a product family named "{brand} plans" in Maxio.</summary>
    private static async Task<ProductFamily> EnsureFamilyAsync(BillingService billing, string brand)
    {
        var familyName = brand + " plans";
        var handle = Regex.Replace(familyName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        async Task<ProductFamily?> FindAsync()
        {
            var families = await billing.Client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest());
            return families.Select(f => f.ProductFamily)
                .FirstOrDefault(f => f != null && f.ArchivedAt == null &&
                    (string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase)));
        }

        var existing = await FindAsync();
        if (existing != null) return existing;
        try
        {
            var created = await billing.Client.ProductFamilies.CreateProductFamily(new CreateProductFamilyOperationRequest
            {
                Body = new CreateProductFamilyRequest { ProductFamily = new CreateProductFamily { Name = familyName, Handle = handle } },
            });
            return created.ProductFamily!;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException)
        {
            // Lost a race with a concurrent request that created the same family.
            var raced = await FindAsync();
            if (raced != null) return raced;
            throw;
        }
    }
}

/// <summary>
/// Prices a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint<IResult, int, CurrencyPriceRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, BillingService billing) =>
            {
                return await HandleAsync(planId, request, billing);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int planId, CurrencyPriceRequest request, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            var currency = request?.Currency?.Trim().ToUpperInvariant();
            if (currency == null || !Regex.IsMatch(currency, "^[A-Z]{3}$"))
                throw BillingException.BadRequest("A three-letter ISO currency code is required.");
            var price = request!.Price;
            if (price == null || price <= 0)
                throw BillingException.BadRequest("A price greater than zero is required.");
            // The Maxio SDK carries currency prices as whole currency units.
            if (decimal.Round(price.Value) != price.Value || price > int.MaxValue)
                throw BillingException.BadRequest("Currency prices must be whole amounts of the currency.");

            var plan = await billing.GetPlanByIdAsync(planId);
            if (plan.ArchivedAt != null)
                throw BillingException.Unprocessable($"Plan {planId} is archived.");
            if (plan.TrialPriceInCents != null || plan.InitialChargeInCents != null)
                throw BillingException.Unprocessable("Plans with a trial or a setup fee need prices for those too; that is not supported.");

            var site = (await billing.Client.Sites.ReadSite()).Site;
            if (string.Equals(site.Currency, currency, StringComparison.OrdinalIgnoreCase))
                throw BillingException.BadRequest($"{currency} is the shop's own currency; the plan is already priced in it.");
            if (site.NonPrimaryCurrencies?.Contains(currency, StringComparer.OrdinalIgnoreCase) != true)
                throw BillingException.BadRequest($"{currency} is not enabled for the billing site. Enabled currencies: " +
                    string.Join(", ", new[] { site.Currency }.Concat(site.NonPrimaryCurrencies ?? Array.Empty<string>()).Where(c => c != null)) + ".");

            var pricePointId = plan.ProductPricePointId ?? plan.DefaultProductPricePointId;
            if (pricePointId == null)
                throw BillingException.Unprocessable($"Plan {planId} has no price point.");

            await billing.Client.ProductPricePoints.CreateProductCurrencyPrices(new CreateProductCurrencyPricesOperationRequest
            {
                ProductPricePointId = pricePointId.Value,
                Body = new CreateProductCurrencyPricesRequest
                {
                    CurrencyPrices = new[]
                    {
                        new CreateProductCurrencyPrice { Currency = currency, Price = (int)price.Value, Role = CurrencyPriceRole.Baseline },
                    },
                },
            });

            return Results.Created($"api/plans/{planId}", new CurrencyPriceDto { PlanId = planId, Currency = currency, Price = price.Value });
        });
    }
}

/// <summary>
/// Sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint<IResult, int, NextBillingDateRequest, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, NextBillingDateRequest request, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, request, billing);
            })
            .Produces<SubscriptionDto>()
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(int subscriptionId, NextBillingDateRequest request, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            if (!DateTime.TryParseExact(request?.ResolvedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw BillingException.BadRequest("A date formatted yyyy-MM-dd is required.");

            var subscription = await billing.GetSubscriptionAsync(subscriptionId);

            // Maxio works in the site's time zone, whose offset shows on the subscription's own timestamps.
            var offset = (subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt ?? subscription.CreatedAt)?.Offset ?? TimeSpan.Zero;
            // Midday, so the date is the same in the site's time zone and in UTC.
            var billAt = new DateTimeOffset(parsed.Date.AddHours(12), offset);
            if (billAt <= DateTimeOffset.UtcNow)
                throw BillingException.BadRequest("The next billing date must be in the future.");

            var updated = await billing.Client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest
                {
                    Subscription = new UpdateSubscription { NextBillingAt = billAt },
                },
            });

            return Results.Ok(BillingService.ToDto(updated.Subscription!));
        });
    }
}
