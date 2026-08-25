using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds, does not capture) the order's total with PayPal, using either a one-off
/// card or a previously-saved card. Money is only actually taken when the order is later fulfilled.
/// </summary>
/// <remarks>
/// The saved-card lookup is done inside <see cref="AddRoute"/>'s route delegate, using an
/// <see cref="IRepository{PaymentMethod}"/> bound as a per-request lambda parameter — NOT
/// constructor-injected. MinimalApi.Endpoint registers endpoint classes as singletons, so a
/// constructor-injected repository (backed by a scoped DbContext) would be captured once at
/// startup and reused for the app's whole lifetime, silently going stale (e.g. a saved card
/// deleted through a different request would still appear to exist here). Only the stateless
/// <see cref="IPaymentProvider"/> is safe to take via the constructor.
/// </remarks>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IRepository<Order>>
{
    private readonly IPaymentProvider _paymentProvider;

    public PayOrderEndpoint(IPaymentProvider paymentProvider)
    {
        _paymentProvider = paymentProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestBody body, ClaimsPrincipal user, IRepository<Order> orderRepository, IRepository<PaymentMethod> paymentMethodRepository) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if ((body.Card is null) == (body.PaymentMethodId is null))
                {
                    return Results.BadRequest(new { message = "Provide exactly one of Card or PaymentMethodId." });
                }

                string? vaultId = null;
                if (body.PaymentMethodId is int paymentMethodId)
                {
                    var paymentMethod = await paymentMethodRepository.GetByIdAsync(paymentMethodId);
                    if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
                    {
                        return Results.BadRequest(new { message = "The specified saved card was not found." });
                    }

                    vaultId = paymentMethod.VaultId;
                }

                return await HandleAsync(new PayOrderRequest(orderId, body.Card, vaultId), user, orderRepository);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderRepository.FirstOrDefaultAsync(new OrderByIdForBuyerSpec(request.OrderId, buyerId));
        if (order is null)
        {
            return Results.NotFound();
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return Results.Conflict(new { message = $"Order {order.Id} is not awaiting payment (status {order.Status})." });
        }

        var cardDetails = request.Card is null
            ? null
            : new CardDetails(
                request.Card.Name,
                request.Card.Number,
                request.Card.Expiry,
                request.Card.SecurityCode,
                request.Card.AddressLine1,
                request.Card.City,
                request.Card.PostalCode,
                request.Card.CountryCode);

        var authorizeRequest = new AuthorizePaymentRequest(
            order.Total(),
            order.Currency,
            $"order-{order.Id}-{order.IdempotencySalt:N}",
            cardDetails,
            request.VaultId);

        var result = await _paymentProvider.AuthorizeAsync(authorizeRequest, CancellationToken.None);

        order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await orderRepository.UpdateAsync(order);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.FromOrder(order)
        });
    }
}
