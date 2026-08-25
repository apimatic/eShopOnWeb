using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepo,
                   IPayPalService payPal,
                   ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId));
                if (order == null || order.BuyerId != buyerId)
                    return Results.NotFound();

                if (order.Status == OrderStatus.Cancelled)
                    return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });

                if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.FullyRefunded)
                    return Results.BadRequest(new { error = $"Cannot cancel an order in status {order.Status}. Use refund instead." });

                if (order.Status == OrderStatus.PaymentAuthorized)
                {
                    try
                    {
                        await payPal.VoidAsync(order.AuthorizationId!);
                    }
                    catch (PayPalException ex)
                    {
                        return Results.BadRequest(new { error = $"Void failed: {ex.Message}" });
                    }
                }

                order.SetVoided();
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new { orderId = order.Id, status = order.Status.ToString() });
            })
            .WithTags("OrderEndpoints");
    }
}
