using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Operator action: cancel an order before fulfilment, releasing any held funds so no money
/// ever moved. Admin-only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(request.OrderId);
        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Ok(response);
    }
}
