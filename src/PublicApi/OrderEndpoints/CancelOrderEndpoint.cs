using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: cancels the order before fulfilment, voiding the hold so the shopper's funds are
/// released and no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service)
    {
        var payment = await service.CancelAsync(request.OrderId);
        return Results.Ok(new OrderPaymentResponse(request.CorrelationId())
        {
            Payment = PaymentStateDto.From(payment)
        });
    }
}
