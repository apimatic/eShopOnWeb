using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

/// <summary>Operator action: cancels an order before fulfilment, releasing any held funds without ever capturing them.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderPaymentService);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        try
        {
            var order = await orderPaymentService.CancelAsync(request.OrderId, default);
            return order is null ? Results.NotFound() : Results.Ok(OrderDto.FromOrder(order));
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
}
