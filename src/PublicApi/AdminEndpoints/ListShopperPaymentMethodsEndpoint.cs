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
/// Lists every card and bank account a shopper has ever saved, removed ones included
/// </summary>
public class ListShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, PaymentMethodService service, CancellationToken ct) =>
            {
                var result = await service.ListAllForSupportAsync(email, ct);
                return Results.Ok(result);
            })
            .Produces<System.Collections.Generic.List<SupportPaymentMethodDto>>()
            .WithTags("AdminEndpoints");
    }
}
