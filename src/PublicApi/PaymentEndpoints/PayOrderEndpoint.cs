using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record PayOrderCommand(string BuyerId, int OrderId, PayOrderRequest Body, CancellationToken Ct);

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total against a one-off card or a saved
/// card. Does not take the money. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new PayOrderCommand(buyerId, orderId, request, http.RequestAborted), service);
            })
            .Produces<PaymentStateDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand command, IPaymentApplicationService service)
    {
        var body = command.Body;
        var instrument = new PaymentInstrumentInput(
            body.Card is null ? null : PaymentMapper.ToDomain(body.Card),
            body.PaymentMethodId,
            PaymentMapper.ToDomain(body.BillingAddress));

        var payment = await service.PayAsync(command.BuyerId, command.OrderId, instrument, command.Ct);
        return Results.Ok(PaymentMapper.ToDto(payment));
    }
}
