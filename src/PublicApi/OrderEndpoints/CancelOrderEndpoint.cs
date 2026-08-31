using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment. Any PayPal hold is voided so the
/// shopper's funds are released and no money ever moves. Repeating the call for an
/// already cancelled order returns its state.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;

    public CancelOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest(), orderId);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId));

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Ok(BuildResponse(request, order, payment));
        }
        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            return Results.Conflict($"Order {orderId} is {order.Status}; only an unfulfilled order can be cancelled. Refund it instead.");
        }

        if (payment?.AuthorizationId != null && payment.AuthorizationStatus != "VOIDED")
        {
            try
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-order-{orderId}-void-{PaymentRunContext.RunId}");
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment);
            }
            catch (PayPalApiException ex)
            {
                return Results.UnprocessableEntity(
                    $"PayPal could not release the hold {payment.AuthorizationId} for order {orderId}: {ex.Message} (debug id: {ex.DebugId}). " +
                    "The order was NOT cancelled; retry the cancel.");
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);

        return Results.Ok(BuildResponse(request, order, payment));
    }

    private static CancelOrderResponse BuildResponse(CancelOrderRequest request, Order order, OrderPayment? payment) =>
        new(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = payment == null ? null : OrderDtoMapper.Map(payment)
        };
}
