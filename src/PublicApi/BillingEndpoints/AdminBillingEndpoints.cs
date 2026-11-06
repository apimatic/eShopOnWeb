using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Staff billing features, for the administrator role.
/// </summary>
public class AdminBillingEndpoints : IEndpoint
{
    private const string TAG = "BillingAdminEndpoints";
    private const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string JWT = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = JWT)] async (CreatePlanRequest request, BillingService billing) =>
            {
                var plan = await billing.CreatePlanAsync(request);
                return Results.Created($"api/plans/{plan.Handle}", plan);
            })
            .Produces<PlanDto>(StatusCodes.Status201Created).WithTags(TAG);

        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = JWT)] async (int planId, CurrencyPriceRequest request, BillingService billing) =>
                Results.Created($"api/admin/plans/{planId}/currency-prices", await billing.AddCurrencyPriceAsync(planId, request)))
            .Produces<CurrencyPriceDto>(StatusCodes.Status201Created).WithTags(TAG);

        app.MapPut("api/admin/subscriptions/{subscriptionId:int}/next-billing-date",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = JWT)] async (int subscriptionId, NextBillingDateRequest request, BillingService billing) =>
                Results.Ok(await billing.SetNextBillingDateAsync(subscriptionId, request?.Date)))
            .Produces<NextBillingDateDto>().WithTags(TAG);
    }
}
