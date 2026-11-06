using System;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Staff features: new plans, currency prices and the next billing date
/// </summary>
public class AdminBillingEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        const string admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
        const string scheme = JwtBearerDefaults.AuthenticationScheme;

        app.MapPost("api/admin/plans",
            [Authorize(Roles = admins, AuthenticationSchemes = scheme)] async
            (CreatePlanRequest request, BillingService billing) =>
            {
                var plan = await billing.CreatePlanAsync(request);
                return Results.Created($"api/plans", new PlanResponse(request.CorrelationId()) { Plan = plan });
            })
            .Produces<PlanResponse>(StatusCodes.Status201Created)
            .WithTags("BillingAdminEndpoints");

        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = admins, AuthenticationSchemes = scheme)] async
            (int planId, CurrencyPriceRequest request, BillingService billing) =>
            {
                var result = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price);
                return Results.Created($"api/admin/plans/{planId}/currency-prices", result);
            })
            .Produces<CurrencyPriceResponse>(StatusCodes.Status201Created)
            .WithTags("BillingAdminEndpoints");

        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = admins, AuthenticationSchemes = scheme)] async
            (int subscriptionId, NextBillingDateRequest request, BillingService billing) =>
            {
                var text = !string.IsNullOrWhiteSpace(request.Date) ? request.Date : request.NextBillingDate;
                // Accept a plain date or the date part of a full timestamp.
                if (text is { Length: > 10 } && text[10] is 'T' or ' ') text = text[..10];
                if (!DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw BillingException.BadRequest("The 'date' is required and must look like 2026-01-31.");
                var subscription = await billing.SetNextBillingDateAsync(subscriptionId, date);
                return Results.Ok(new SubscriptionDetailResponse(request.CorrelationId()) { Subscription = subscription });
            })
            .Produces<SubscriptionDetailResponse>()
            .WithTags("BillingAdminEndpoints");
    }
}
