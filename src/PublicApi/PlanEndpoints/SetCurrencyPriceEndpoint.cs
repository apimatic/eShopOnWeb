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
public class SetCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId:int}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, CancellationToken ct, BillingService billing) =>
            {
                return Results.Ok(await billing.SetCurrencyPriceAsync(planId, request, ct));
            })
            .Produces<CurrencyPriceDto>()
            .WithTags("PlanEndpoints");
    }
}
