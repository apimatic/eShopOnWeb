using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using BlazorShared.Authorization;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, IRepository<PaymentInfo> paymentRepo, IPayPalService payPal) =>
            {
                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
                if (order == null) return Results.NotFound();

                if (order.Status == OrderStatus.Cancelled)
                    return Results.Ok(new CancelOrderResponse { OrderId = orderId, Status = "Cancelled" });

                if (order.Status != OrderStatus.Authorized)
                    return Results.Conflict($"Only Authorized orders can be cancelled. Current status: {order.Status}.");

                var payment = order.Payment;
                if (payment?.AuthorizationId != null)
                {
                    await payPal.VoidPaymentAsync(payment.AuthorizationId);
                }

                order.UpdateStatus(OrderStatus.Cancelled);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new CancelOrderResponse { OrderId = orderId, Status = "Cancelled" });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
