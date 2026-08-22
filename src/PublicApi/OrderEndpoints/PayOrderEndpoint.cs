using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _payPalSettings;

    public PayOrderEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.RequireBuyerId(user);
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
        => HandleAsync(request, orderPaymentService, CancellationToken.None);

    private async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var order = await orderPaymentService.PayAsync(
            new PayOrderCommand(
                request.BuyerId,
                request.OrderId,
                request.PaymentMethodId,
                request.Card?.ToInput()),
            cancellationToken);

        var response = new OrderActionResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, _payPalSettings.Currency)
        };
        return Results.Ok(response);
    }
}
