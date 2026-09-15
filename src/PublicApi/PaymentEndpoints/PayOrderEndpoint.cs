using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total. Pays with a one-off card or
/// one of the shopper's saved cards. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service,
                CancellationToken ct) =>
            {
                return await HandleAsync(
                    new PayOrderCommand(orderId, PaymentUser.BuyerId(user), request, ct), service);
            })
            .Produces<OrderSummaryDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand command, IOrderPaymentService service)
    {
        var request = command.Request;

        var hasCard = request.Card is not null;
        var hasSaved = request.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            return Results.BadRequest(
                "Provide exactly one of 'card' (a one-off card) or 'savedPaymentMethodId' (a saved card).");
        }

        CardDetails? card = hasCard ? PaymentApiMapper.ToCardDetails(request.Card!) : null;

        var order = await service.AuthorizeAsync(command.OrderId, command.BuyerId, card,
            request.SavedPaymentMethodId, command.Ct);

        return Results.Ok(PaymentApiMapper.ToOrderSummaryDto(order));
    }
}

public record PayOrderCommand(int OrderId, string BuyerId, PayOrderRequest Request, CancellationToken Ct);
