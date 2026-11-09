using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.AdminEndpoints;

/// <summary>
/// Onboards a seller as a new merchant
/// </summary>
public class CreateSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSellerRequest request, InvoiceAndSellerService service, CancellationToken ct) =>
            {
                var result = await service.OnboardSellerAsync(request, ct);
                return Results.Created($"api/admin/sellers/{result.MerchantId}", result);
            })
            .Produces<SellerDto>()
            .WithTags("AdminEndpoints");
    }
}
