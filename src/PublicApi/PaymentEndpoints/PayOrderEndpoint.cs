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
/// Authorizes (holds) an order's total — with a one-off card or one of the shopper's saved cards. Does not
/// take the money. Idempotent under a double-click.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;
    private readonly ISavedCardService _savedCards;

    public PayOrderEndpoint(IHttpContextAccessor http, ISavedCardService savedCards)
    {
        _http = http;
        _savedCards = savedCards;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentSummary>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var buyerId = CallerIdentity.BuyerId(ctx.User);
        var ct = ctx.RequestAborted;

        PaymentInstrument instrument;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            // Ownership-checked resolution: a shopper can only pay with their own saved card.
            var vaultId = await _savedCards.ResolveVaultIdAsync(buyerId, paymentMethodId, ct);
            instrument = PaymentInstrument.FromVault(vaultId);
        }
        else if (request.Card is not null)
        {
            instrument = PaymentInstrument.FromCard(request.Card.ToCardDetails());
        }
        else
        {
            throw PaymentException.Validation("Supply either a card or a saved paymentMethodId to pay.");
        }

        var summary = await service.PayAsync(buyerId, request.OrderId, instrument, ct);
        return Results.Ok(summary);
    }
}
