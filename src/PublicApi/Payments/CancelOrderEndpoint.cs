using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the shopper's held funds so no money moves.
/// Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IPaymentService paymentService) =>
            {
                var request = new CancelOrderRequest { OrderId = orderId, Cancellation = http.RequestAborted };
                return await HandleAsync(request, paymentService);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService paymentService)
    {
        await paymentService.CancelOrderAsync(request.OrderId, request.Cancellation);

        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            PaymentStatus = "Cancelled"
        };
        return Results.Ok(response);
    }
}

public class CancelOrderRequest : PaymentRequestBase
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}
