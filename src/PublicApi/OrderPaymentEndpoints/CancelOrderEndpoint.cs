using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the held funds so no money ever
/// moved. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public CancelOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), service);
            })
            .Produces<OrderDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(request.OrderId);
        return Results.Ok(PaymentMapper.ToOrderDto(order, _settings.Currency));
    }
}
