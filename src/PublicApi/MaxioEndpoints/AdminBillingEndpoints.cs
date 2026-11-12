using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

public record CreatePlanRequest(string? Name, decimal? Price, int? BrandId, string? Brand);

public record CurrencyPriceRequest(string? Currency, decimal? Price);

public record NextBillingDateRequest(string? NextBillingDate);

/// <summary>Staff (administrator role) billing endpoints: add plans, price them in other currencies, set next billing date.</summary>
public class AdminBillingEndpoints : IEndpoint
{
    private const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)]
            (CreatePlanRequest request, MaxioBillingService billing, HttpContext http) =>
                MaxioEndpointSupport.RunAsync(billing, http,
                    ct => billing.CreatePlanAsync(request.Name, request.Price, request.BrandId, request.Brand, ct),
                    r => Results.Created("api/plans", r)))
            .Produces<CreatedPlanDto>(201).WithTags("MaxioEndpoints");

        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)]
            (int planId, CurrencyPriceRequest request, MaxioBillingService billing, HttpContext http) =>
                MaxioEndpointSupport.RunAsync(billing, http, ct => billing.SetCurrencyPriceAsync(planId, request.Currency, request.Price, ct)))
            .Produces<CurrencyPriceDto>().WithTags("MaxioEndpoints");

        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)]
            (int subscriptionId, NextBillingDateRequest request, MaxioBillingService billing, HttpContext http) =>
                MaxioEndpointSupport.RunAsync(billing, http, ct => billing.SetNextBillingDateAsync(subscriptionId, request.NextBillingDate, ct)))
            .Produces<NextBillingDto>().WithTags("MaxioEndpoints");
    }
}
