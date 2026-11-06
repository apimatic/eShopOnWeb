using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(billing, ct);
            })
            .Produces<ListPlansResponse>()
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(BillingService billing, CancellationToken ct)
    {
        return Results.Ok(new ListPlansResponse(Guid.NewGuid()) { Plans = await billing.ListPlansAsync(ct) });
    }
}

/// <summary>
/// Staff: adds a plan
/// </summary>
public class CreatePlanEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePlanRequest request, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(request, billing, ct);
            })
            .Produces<PlanResponse>(StatusCodes.Status201Created)
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePlanRequest request, BillingService billing, CancellationToken ct)
    {
        if (request == null) throw BillingException.BadRequest("A request body is required.");
        var plan = await billing.CreatePlanAsync(request.Name, request.MonthlyPrice, request.CatalogBrandId, ct);
        return Results.Created($"api/plans/{plan.Handle}", new PlanResponse(request.CorrelationId()) { Plan = plan });
    }
}

/// <summary>
/// Staff: prices a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long planId, CreateCurrencyPriceRequest request, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(planId, request, billing, ct);
            })
            .Produces<CurrencyPriceResponse>(StatusCodes.Status201Created)
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(long planId, CreateCurrencyPriceRequest request, BillingService billing, CancellationToken ct)
    {
        if (request == null) throw BillingException.BadRequest("A request body is required.");
        var result = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, request.CorrelationId(), ct);
        return Results.Created($"api/admin/plans/{planId}/currency-prices", result);
    }
}

/// <summary>
/// Staff: sets the date a subscriber is next billed
/// </summary>
public class SetNextBillingDateEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/admin/subscriptions/{subscriptionId}/next-billing-date",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (long subscriptionId, SetNextBillingDateRequest request, BillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(subscriptionId, request, billing, ct);
            })
            .Produces<SubscriptionResponse>()
            .WithTags("PlanEndpoints");
    }

    public async Task<IResult> HandleAsync(long subscriptionId, SetNextBillingDateRequest request, BillingService billing, CancellationToken ct)
    {
        if (request == null) throw BillingException.BadRequest("A request body is required.");
        var dto = await billing.SetNextBillingDateAsync(subscriptionId, request.Date, ct);
        return Results.Ok(new SubscriptionResponse(request.CorrelationId()) { Subscription = dto });
    }
}
