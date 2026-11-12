using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    /// <summary>Monthly price in the site's main currency.</summary>
    public decimal? Price { get; set; }
    /// <summary>Id of a brand of the shop's catalog.</summary>
    public int? BrandId { get; set; }
    public string? Description { get; set; }
}

public class CreateCurrencyPriceRequest : BaseRequest
{
    /// <summary>Three-letter ISO currency code.</summary>
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class SetNextBillingDateRequest : BaseRequest
{
    /// <summary>The next billing date, YYYY-MM-DD.</summary>
    public string? Date { get; set; }
    /// <summary>Alias of <see cref="Date"/>.</summary>
    public string? NextBillingDate { get; set; }
}

/// <summary>
/// Staff add a plan
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, IMaxioBillingService billing, CancellationToken ct) =>
            {
                if (request.Price is null) throw BillingException.BadRequest("The monthly price is required.");
                if (request.BrandId is null) throw BillingException.BadRequest("The brand is required.");
                var plan = await billing.CreatePlanAsync(request.Name ?? string.Empty, request.Price.Value, request.BrandId.Value, request.Description, ct);
                return Results.Created($"api/plans", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created)
            .WithTags("AdminPlanEndpoints");
    }
}

/// <summary>
/// Staff price a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CreateCurrencyPriceRequest request, IMaxioBillingService billing, CancellationToken ct) =>
            {
                if (request.Price is null) throw BillingException.BadRequest("The price is required.");
                var price = await billing.SetCurrencyPriceAsync(planId, request.Currency ?? string.Empty, request.Price.Value, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices", price);
            })
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created)
            .WithTags("AdminPlanEndpoints");
    }
}

/// <summary>
/// Staff set the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, SetNextBillingDateRequest request, IMaxioBillingService billing, CancellationToken ct) =>
            {
                var date = request.Date ?? request.NextBillingDate;
                if (string.IsNullOrWhiteSpace(date)) throw BillingException.BadRequest("The date is required.");
                return Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, date.Trim(), ct));
            })
            .Produces<SubscriptionDto>()
            .WithTags("AdminSubscriptionEndpoints");
    }
}
