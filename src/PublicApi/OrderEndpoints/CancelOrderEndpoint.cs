using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepo,
                   IRepository<OrderPayment> paymentRepo,
                   IPayPalService paypal) =>
            {
                var order = await orderRepo.GetByIdAsync(orderId);
                if (order == null) return Results.NotFound($"Order {orderId} not found.");

                var payment = await paymentRepo.FirstOrDefaultAsync(
                    new OrderPaymentByOrderIdSpec(orderId));

                if (payment == null)
                    return Results.Problem("No payment record for this order.", statusCode: 404);

                // Idempotency
                if (payment.Status == PaymentStatus.Voided)
                    return Results.Ok(new CancelOrderResponse { OrderId = orderId, Status = "Voided" });

                if (payment.Status != PaymentStatus.Authorized)
                    return Results.Problem(
                        $"Order is in state '{payment.Status}' and cannot be cancelled.",
                        statusCode: 409);

                await paypal.VoidAsync(payment.PayPalAuthorizationId!);
                payment.SetVoided();
                await paymentRepo.UpdateAsync(payment);

                return Results.Ok(new CancelOrderResponse { OrderId = orderId, Status = "Voided" });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IPayPalService dependency)
        => throw new NotImplementedException();
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
}
