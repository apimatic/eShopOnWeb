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

public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, IRepository<PaymentInfo> paymentRepo, IPayPalService payPal) =>
            {
                var order = await orderRepo.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
                if (order == null) return Results.NotFound();

                if (order.Status == OrderStatus.Fulfilled)
                    return Results.Ok(new FulfilOrderResponse { OrderId = orderId, Status = "Fulfilled" });

                if (order.Status != OrderStatus.Authorized)
                    return Results.Conflict($"Order must be Authorized to fulfil. Current status: {order.Status}.");

                var payment = order.Payment;
                if (payment?.AuthorizationId == null)
                    return Results.Conflict("No authorization found for this order.");

                var idempotencyKey = $"capture-{orderId}";
                var captureResult = await payPal.CapturePaymentAsync(payment.AuthorizationId, idempotencyKey);

                payment.SetCapture(captureResult.CaptureId, captureResult.CapturedAmount, captureResult.Fee, captureResult.NetAmount);
                await paymentRepo.UpdateAsync(payment);

                order.UpdateStatus(OrderStatus.Fulfilled);
                await orderRepo.UpdateAsync(order);

                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = orderId,
                    Status = "Fulfilled",
                    CaptureId = captureResult.CaptureId,
                    CapturedAmount = captureResult.CapturedAmount,
                    Currency = captureResult.Currency
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
