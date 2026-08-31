using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }
}

/// <summary>
/// Operator action: cancels an order before fulfilment. Any held funds are released
/// (the PayPal authorization is voided), so no money ever moves.
/// Idempotent: cancelling an already-cancelled order returns its current state.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             IRepository<Order> orderRepository,
             IRepository<Payment> paymentRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(orderId, orderRepository, paymentRepository, payPalClient);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId,
        IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IPayPalClient payPalClient)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound(new { message = $"Order {orderId} not found." });
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Ok(new CancelOrderResponse { OrderId = order.Id, Status = order.Status.ToString(), PaymentStatus = PaymentStatus.Voided.ToString() });
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return Results.Conflict(new { message = $"Order {orderId} has already been fulfilled; refund it instead of cancelling." });
        }

        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
        if (payment != null && payment.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            try
            {
                await payPalClient.VoidAuthorizationAsync(payment.AuthorizationId, $"void-{payment.ClientToken:N}");
                payment.MarkVoided();
            }
            catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Already voided/captured on PayPal's side - treat as released.
                payment.MarkVoided();
            }

            await paymentRepository.UpdateAsync(payment);
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order);

        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PaymentStatus = payment?.Status.ToString()
        });
    }
}
