using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

/// <summary>
/// Cancels an order before fulfilment and releases the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public CancelOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId));
            })
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        var order = await _paymentService.CancelOrderAsync(request.OrderId);
        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            Payment = order.Payment is null ? null : PaymentDtoMapper.ToDto(order.Payment),
            Order = PaymentDtoMapper.ToDto(order)
        };

        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}
