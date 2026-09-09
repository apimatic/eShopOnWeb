using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing any held funds so no money moved.
/// Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OperatorOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public CancelOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OperatorOrderRequest(orderId, ct), service);
            })
            .Produces<OrderPaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OperatorOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(request.OrderId, request.Cancellation);
        return Results.Ok(OrderPaymentDto.From(order, _settings.Currency));
    }
}
