using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the held funds. No money moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public CancelOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) => await HandleAsync(new OrderActionRequest { OrderId = orderId }, service))
            .Produces<OrderPaymentSummary>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service)
    {
        var summary = await service.CancelAsync(request.OrderId, _http.HttpContext!.RequestAborted);
        return Results.Ok(summary);
    }
}
