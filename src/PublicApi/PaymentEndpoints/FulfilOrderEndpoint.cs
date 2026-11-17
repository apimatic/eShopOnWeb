using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Captures the authorized payment when an operator fulfils an order.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId));
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        var order = await _paymentService.FulfilOrderAsync(request.OrderId);
        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            Payment = order.Payment is null ? null : PaymentDtoMapper.ToDto(order.Payment),
            Order = PaymentDtoMapper.ToDto(order)
        };

        return Results.Ok(response);
    }
}
