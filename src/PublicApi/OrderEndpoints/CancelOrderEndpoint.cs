using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record CancelOrderCommand(int OrderId);

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action. Cancels before fulfilment: the held funds are
/// released, so no money ever moved. Idempotent: cancelling an already-cancelled order succeeds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderCommand, IPaymentService>
{
    private readonly IPaymentSettings _settings;

    public CancelOrderEndpoint(IPaymentSettings settings)
    {
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new CancelOrderCommand(orderId), paymentService))
            .Produces<OrderPaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderCommand command, IPaymentService paymentService)
    {
        var order = await paymentService.CancelAsync(command.OrderId);
        return Results.Ok(PaymentDtoMapper.ToDto(order, _settings.Currency));
    }
}
