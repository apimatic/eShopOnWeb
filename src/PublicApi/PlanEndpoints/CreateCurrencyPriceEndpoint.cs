using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PlanEndpoints;

/// <summary>
/// Staff: prices a plan in another currency
/// </summary>
public class CreateCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CreateCurrencyPriceRequest? request, MaxioBillingService billing, CancellationToken ct) =>
            {
                if (request is null) throw BillingException.BadRequest("A request body is required.");
                var prices = await billing.AddCurrencyPriceAsync(planId, request.Currency, request.Price, ct);
                return Results.Created($"api/admin/plans/{planId}/currency-prices", new { planId, currencyPrices = prices });
            })
            .WithTags("PlanEndpoints");
    }
}
