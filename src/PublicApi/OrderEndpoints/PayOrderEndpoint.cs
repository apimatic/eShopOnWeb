using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds, does not capture) an order's total by card or by a saved payment method.
/// Idempotent: an order that already has a payment is returned unchanged rather than re-authorized.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService paymentService,
                CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentService, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService, CancellationToken ct)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        CardDetails? card = request.Card is null
            ? null
            : new CardDetails(request.Card.Number, request.Card.Expiry, request.Card.SecurityCode,
                request.Card.CardholderName, request.Card.AddressLine1, request.Card.AddressLine2,
                request.Card.City, request.Card.State, request.Card.PostalCode, request.Card.CountryCode);

        var order = await paymentService.PayAsync(request.BuyerId, request.OrderId, card,
            request.SavedPaymentMethodId, ct);
        if (order is null) return Results.NotFound();

        response.OrderId = order.Id;
        response.Order = OrderMapper.ToDto(order);
        return Results.Ok(response);
    }
}
