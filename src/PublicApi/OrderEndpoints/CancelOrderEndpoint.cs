using System;
using System.Security.Claims;
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

/// <summary>
/// Operator action: cancels an order before fulfilment. Any held funds are released
/// (the PayPal authorization is voided), so no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;

    public CancelOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
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
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, user);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ClaimsPrincipal user)
    {
        var response = new CancelOrderResponse(request.CorrelationId());

        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        // Idempotency: cancelling an already-cancelled order is a no-op.
        if (order.Status == OrderStatus.Cancelled)
        {
            response.OrderId = order.Id;
            response.OrderStatus = order.Status.ToString();
            return Results.Ok(response);
        }
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return Results.Conflict($"Order {order.Id} is {order.Status}; issue a refund instead of cancelling.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(order.Id));
        if (payment is not null && payment.Status == PaymentStatus.Authorized)
        {
            try
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId!, $"eshop-payment-{payment.Reference}-void");
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment);
            }
            catch (PaymentGatewayException ex)
            {
                return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);

        response.OrderId = order.Id;
        response.OrderStatus = order.Status.ToString();
        response.Payment = payment is null ? null : PaymentDto.FromEntity(payment);
        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}
