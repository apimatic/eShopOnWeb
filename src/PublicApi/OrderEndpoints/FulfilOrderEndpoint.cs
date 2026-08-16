using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record FulfilOrderCommand(int OrderId);

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action. Captures the held money (renewing a stale hold
/// first if needed). Afterwards the payment shows PayPal's captured amount, fee and net proceeds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderCommand, IPaymentService>
{
    private readonly IPaymentSettings _settings;

    public FulfilOrderEndpoint(IPaymentSettings settings)
    {
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new FulfilOrderCommand(orderId), paymentService))
            .Produces<OrderPaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderCommand command, IPaymentService paymentService)
    {
        var order = await paymentService.FulfilAsync(command.OrderId);
        return Results.Ok(PaymentDtoMapper.ToDto(order, _settings.Currency));
    }
}
