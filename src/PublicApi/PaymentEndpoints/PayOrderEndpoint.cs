using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly CatalogContext _db;
    private readonly FinixPaymentService _payments;

    public PayOrderEndpoint(CatalogContext db, FinixPaymentService payments)
    {
        _db = db;
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, user);
                })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return PaymentResults.Unauthorized();
        }

        var order = await OrderQueries.LoadOrderAsync(_db, request.OrderId, CancellationToken.None);
        if (order is null || !string.Equals(order.BuyerId, buyerId, System.StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        try
        {
            var payment = await OrderQueries.LoadPaymentAsync(_db, order.Id, CancellationToken.None)
                          ?? await _payments.CreatePaymentAsync(order, CancellationToken.None);

            var paymentMethodId = request.EffectivePaymentMethodId;
            CardDetails? card = null;

            if (string.IsNullOrWhiteSpace(paymentMethodId))
            {
                var cardRequest = request.EffectiveCard;
                if (cardRequest is null)
                {
                    return Results.BadRequest(new
                    {
                        message = "Provide card details or a saved paymentMethodId."
                    });
                }

                card = cardRequest.ToCardDetails();
            }

            await _payments.PayAsync(order, payment, paymentMethodId, card, CancellationToken.None);
            var refunds = await OrderQueries.LoadRefundsAsync(_db, payment.Id, CancellationToken.None);
            return Results.Ok(PaymentResponseMapper.ToOrderResponse(order, payment, refunds));
        }
        catch (PaymentOperationException exception)
        {
            return PaymentResults.FromException(exception);
        }
    }
}
