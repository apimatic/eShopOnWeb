using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
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

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    private readonly PayPalSettings _payPalSettings;

    public FulfilOrderEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, service, cancellationToken);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IOrderPaymentService orderPaymentService)
        => HandleAsync(orderId, orderPaymentService, CancellationToken.None);

    private async Task<IResult> HandleAsync(int orderId, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var order = await orderPaymentService.FulfilAsync(orderId, cancellationToken);
        return Results.Ok(new OrderActionResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, _payPalSettings.Currency)
        });
    }
}
