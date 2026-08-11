using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, voiding the authorization so the shopper's
/// held funds are released and no money ever moves. Administrator only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    private readonly IPaymentConfiguration _paymentConfiguration;

    public CancelOrderEndpoint(IPaymentConfiguration paymentConfiguration)
    {
        _paymentConfiguration = paymentConfiguration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service);
            })
            .Produces<OrderSummaryDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelOrderAsync(request.OrderId);
        return Results.Ok(OrderMapper.ToSummary(order, _paymentConfiguration.Currency));
    }
}
