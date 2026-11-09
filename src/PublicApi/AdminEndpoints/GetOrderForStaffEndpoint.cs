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
/// Shows staff an order with its payment and refund status
/// </summary>
public class GetOrderForStaffEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service, CancellationToken ct) =>
            {
                var result = await service.GetOrderForStaffAsync(orderId, ct);
                return Results.Ok(result);
            })
            .Produces<AdminOrderDto>()
            .WithTags("AdminEndpoints");
    }
}
