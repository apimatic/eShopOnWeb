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
/// Lists the plans a shopper can subscribe to
/// </summary>
public class ListPlansEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                return await HandleAsync(user, service, ct);
            })
            .Produces<ListPlansResponse>()
            .WithTags("BillingEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, BillingService service, CancellationToken ct)
    {
        
        return Results.Ok(await service.ListPlansAsync(ct));
    }
}
