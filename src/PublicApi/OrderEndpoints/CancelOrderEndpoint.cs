using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the shopper's
/// held funds so no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly IReadRepository<ApplicationCore.Entities.PaymentAggregate.Payment> _paymentRepository;

    public CancelOrderEndpoint(IOrderPaymentService orderPaymentService,
        IReadRepository<ApplicationCore.Entities.PaymentAggregate.Payment> paymentRepository)
    {
        _orderPaymentService = orderPaymentService;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        var response = new CancelOrderResponse(request.CorrelationId());

        var order = await _orderPaymentService.CancelOrderAsync(request.OrderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));

        response.OrderId = order.Id;
        response.OrderStatus = order.Status.ToString();
        response.AuthorizationStatus = payment?.AuthorizationStatus;
        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) {}
    public CancelOrderResponse() {}

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? AuthorizationStatus { get; set; }
}
