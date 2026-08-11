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
/// Authorizes (holds) the order total with PayPal — with either one-off card details or one of
/// the caller's saved cards. Does not capture. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public PayOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = PaymentMapper.GetBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<OrderDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var input = new AuthorizePaymentInput(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);
        var order = await service.AuthorizeAsync(request.BuyerId, request.OrderId, input);
        return Results.Ok(PaymentMapper.ToOrderDto(order, _settings.Currency));
    }
}
