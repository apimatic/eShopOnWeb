using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public PaymentStateDto? Payment { get; set; }
}

/// <summary>Cancels an order before fulfilment, releasing any held funds. Operator action.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(request.OrderId);
        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = PaymentStateDto.From(order.Payment)
        });
    }
}
