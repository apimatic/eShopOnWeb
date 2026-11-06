using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
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
    public ListPlansResponse() { }
    public ListPlansResponse(System.Guid correlationId) : base(correlationId) { }
    public List<PlanDto> Plans { get; set; } = new();
}

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    /// <summary>Alias of Price.</summary>
    public decimal? MonthlyPrice { get; set; }
    public int? BrandId { get; set; }
}

public class CreateCurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

internal static class BillingUser
{
    public const string JWT = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The caller's identity comes from the JWT.</summary>
    public static string Name(ClaimsPrincipal user)
    {
        var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BillingException(401, "The token does not identify a user.");
        }
        return name;
    }
}

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = BillingUser.JWT)] async (BillingService billing, CancellationToken ct) =>
            {
                var response = new ListPlansResponse { Plans = await billing.ListPlansAsync(ct) };
                return Results.Ok(response);
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }
}

/// <summary>
/// Staff add a plan
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingUser.JWT)] async
            (CreatePlanRequest request, BillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlanAsync(request.Name, request.Price ?? request.MonthlyPrice, request.BrandId, ct);
                return Results.Created("api/plans", plan);
            })
            .Produces<PlanDto>(201)
            .WithTags("BillingEndpoints");
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
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = BillingUser.JWT)] async
            (long planId, CreateCurrencyPriceRequest request, BillingService billing, CancellationToken ct) =>
            {
                await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices",
                    new { planId, currency = request.Currency!.Trim().ToUpperInvariant(), price = request.Price });
            })
            .WithTags("BillingEndpoints");
    }
}
