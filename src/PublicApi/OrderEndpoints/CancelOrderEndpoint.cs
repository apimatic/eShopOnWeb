using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Operator action: cancels the order before fulfilment, releasing the
/// shopper's held funds. No money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IRepository<Order> _orderRepository;

    public CancelOrderEndpoint(IPaymentService paymentService, IRepository<Order> orderRepository)
    {
        _paymentService = paymentService;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId));
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        if (await _orderRepository.GetByIdAsync(request.OrderId) is null)
        {
            return Results.NotFound();
        }

        // Null payment: the order was cancelled before any payment was taken.
        var payment = await _paymentService.CancelOrderPaymentAsync(request.OrderId);

        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Cancelled",
            Payment = payment is null ? null : PaymentDto.FromPayment(payment)
        });
    }
}
