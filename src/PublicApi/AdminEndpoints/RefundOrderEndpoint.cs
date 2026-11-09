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
/// Refunds an order in full, whether it was paid by card or by bank account
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service, CancellationToken ct) =>
            {
                var result = await service.RefundAsync(orderId, ct);
                return result.RefundStatus == "refunded" ? Results.Ok(result) : Results.Accepted($"api/admin/orders/{orderId}", result);
            })
            .Produces<RefundDto>()
            .WithTags("AdminEndpoints");
    }
}
