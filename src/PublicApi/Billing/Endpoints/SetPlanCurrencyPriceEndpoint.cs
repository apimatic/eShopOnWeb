using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Billing.Endpoints;

/// <summary>
/// Staff: prices a plan in another currency
/// </summary>
public class SetPlanCurrencyPriceEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/plans/{planId}/currency-prices",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int planId, CurrencyPriceRequest request, ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                return await HandleAsync(planId, request, user, service, ct);
            })
            .Produces<PlanCurrencyPricesResponse>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(int planId, CurrencyPriceRequest request, ClaimsPrincipal user, BillingService service, CancellationToken ct)
    {
        
        return Results.Ok(await service.SetCurrencyPriceAsync(planId, request.Currency, request.Price, ct));
    }
}
