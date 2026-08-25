using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IRepository<Order> orderRepo,
                   IReadRepository<SavedPaymentMethod> pmRepo,
                   IPayPalService paypal, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? "";
                return await HandleAsync(request, orderRepo, pmRepo, paypal, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> orderRepo,
        IReadRepository<SavedPaymentMethod> pmRepo, IPayPalService paypal, CancellationToken ct)
    {
        var spec = new OrderWithPaymentSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);

        if (order is null) return Results.NotFound();
        if (order.BuyerId != request.BuyerId) return Results.Forbid();
        if (order.Status != OrderStatus.PendingPayment)
            return Results.BadRequest($"Order is in status '{order.Status}' and cannot be paid.");

        var idempotencyKey = $"order-{order.Id}";
        var currency = paypal.Currency;
        PayPal.AuthorizationResult authResult;

        try
        {
            if (!string.IsNullOrEmpty(request.SavedCardId))
            {
                // SavedCardId is the internal paymentMethodId (int); look up the PayPal vault token.
                if (!int.TryParse(request.SavedCardId, out var pmId))
                    return Results.BadRequest("savedCardId must be the numeric payment method ID from GET /api/payment-methods.");

                var pmSpec = new SavedPaymentMethodByIdAndUserSpec(pmId, request.BuyerId);
                var saved = await pmRepo.FirstOrDefaultAsync(pmSpec, ct);
                if (saved is null)
                    return Results.NotFound("Saved payment method not found or does not belong to this user.");

                authResult = await paypal.AuthorizeWithVaultAsync(
                    amount: order.Total(),
                    currency: currency,
                    idempotencyKey: idempotencyKey,
                    vaultToken: saved.VaultToken,
                    invoiceRef: order.Id.ToString(),
                    ct: ct);
            }
            else
            {
                if (request.Card is null)
                    return Results.BadRequest("Either card details or a saved card ID must be provided.");

                authResult = await paypal.AuthorizeWithCardAsync(
                    amount: order.Total(),
                    currency: currency,
                    idempotencyKey: idempotencyKey,
                    card: new CardPaymentDetails(
                        Number: request.Card.Number,
                        ExpiryYear: request.Card.ExpiryYear,
                        ExpiryMonth: request.Card.ExpiryMonth,
                        Cvv: request.Card.Cvv,
                        CardholderName: request.Card.CardholderName,
                        Street: request.Card.Street,
                        City: request.Card.City,
                        State: request.Card.State,
                        PostalCode: request.Card.PostalCode,
                        CountryCode: request.Card.CountryCode),
                    invoiceRef: order.Id.ToString(),
                    ct: ct);
            }
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                title: "Payment authorization failed",
                detail: ex.Message,
                statusCode: ex.StatusCode);
        }

        order.SetPaymentAuthorized(
            authResult.PayPalOrderId,
            authResult.AuthorizationId,
            authResult.AuthorizationStatus,
            authResult.ExpirationTime);

        await orderRepo.UpdateAsync(order, ct);

        return Results.Ok(new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            AuthorizationId = authResult.AuthorizationId,
            Status = authResult.AuthorizationStatus
        });
    }

}
