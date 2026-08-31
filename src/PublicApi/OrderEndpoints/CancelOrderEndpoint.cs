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
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment. Any authorization hold is
/// voided at PayPal, releasing the shopper's held funds — no money ever moves.
/// Idempotent: re-cancelling returns the current state.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<CancelOrderEndpoint> _logger;

    public CancelOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway,
        ILogger<CancelOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(new CancelOrderRequest(orderId)))
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} not found." });
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id));

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Ok(new CancelOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                AuthorizationStatus = payment?.AuthorizationStatus
            });
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            return Results.Conflict(new
            {
                message = $"Order {order.Id} is already fulfilled; issue a refund (POST api/orders/{order.Id}/refunds) instead."
            });
        }

        if (payment?.AuthorizationId is not null)
        {
            try
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    $"eshop-order-{order.Id}-void");
                payment.SetAuthorization(payment.AuthorizationId, "VOIDED", payment.AuthorizationExpiresAt);
                await _paymentRepository.UpdateAsync(payment);
            }
            catch (PayPalApiException ex)
            {
                _logger.LogWarning("Voiding authorization for order {OrderId} failed: {Error} {Issue} (debug {DebugId})",
                    order.Id, ex.ErrorName, ex.Issue, ex.DebugId);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);

        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            AuthorizationStatus = payment?.AuthorizationStatus
        });
    }
}
