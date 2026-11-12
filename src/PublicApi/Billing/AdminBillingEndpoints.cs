using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public class CreatePlanRequest : BaseRequest
{
    public string? Name { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public string? Brand { get; set; }
}

public class CurrencyPriceRequest : BaseRequest
{
    public string? Currency { get; set; }
    public decimal? Price { get; set; }
}

public class NextBillingDateRequest : BaseRequest
{
    /// <summary>The next billing date, YYYY-MM-DD.</summary>
    public string? Date { get; set; }
}

/// <summary>Staff-only billing administration: new plans, currency prices and next billing dates.</summary>
public class AdminBillingEndpoints : IEndpoint
{
    private const string Admin = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = Admin, AuthenticationSchemes = Jwt)] async (CreatePlanRequest request, MaxioBillingService billing, CancellationToken ct) =>
            {
                var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.Brand, ct);
                return Results.Created($"api/plans", plan);
            })
            .WithTags("AdminBillingEndpoints");

        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = Admin, AuthenticationSchemes = Jwt)] async (int planId, CurrencyPriceRequest request, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetCurrencyPriceAsync(planId, request.Currency, request.Price, ct)))
            .WithTags("AdminBillingEndpoints");

        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = Admin, AuthenticationSchemes = Jwt)] async (int subscriptionId, NextBillingDateRequest request, MaxioBillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request.Date, ct)))
            .WithTags("AdminBillingEndpoints");
    }
}
