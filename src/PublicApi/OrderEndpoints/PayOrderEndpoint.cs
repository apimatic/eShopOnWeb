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
using Microsoft.eShopWeb.Infrastructure;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Authorizes payment for an order — puts a hold on the funds.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                PayOrderRequest request,
                IRepository<Order> orderRepo,
                IRepository<OrderPayment> paymentRepo,
                IRepository<PaymentMethod> pmRepo,
                IPayPalPaymentService payPalService,
                PayPalSettings settings,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = OrderHelpers.GetBuyerId(user);
                var order = await orderRepo.GetBySpecAsync(new OrderByIdWithPaymentSpec(orderId), ct);

                if (order == null || order.BuyerId != buyerId)
                    return Results.NotFound(new { error = "Order not found." });

                if (order.Payment != null && order.Payment.PaymentStatus != PaymentStatuses.Pending)
                    return Results.Conflict(new { error = "Order is not in a payable state." });

                var currency = settings.Currency;
                var amount = order.Total();

                AuthorizeResult authResult;
                try
                {
                    if (request.PaymentMethodId.HasValue)
                    {
                        var pm = await pmRepo.GetBySpecAsync(new PaymentMethodByIdSpec(request.PaymentMethodId.Value), ct);
                        if (pm == null || pm.BuyerId != buyerId)
                            return Results.NotFound(new { error = "Payment method not found." });
                        authResult = await payPalService.AuthorizeWithVaultAsync(orderId, amount, currency, pm.PayPalVaultId, ct);
                    }
                    else if (request.Card != null)
                    {
                        var card = new CardDetails(
                            request.Card.Number,
                            request.Card.Expiry,
                            request.Card.SecurityCode,
                            request.Card.Name,
                            request.Card.CountryCode ?? "US");
                        authResult = await payPalService.AuthorizeAsync(orderId, amount, currency, card, ct);
                    }
                    else
                    {
                        return Results.BadRequest(new { error = "Provide either 'card' or 'paymentMethodId'." });
                    }
                }
                catch (PayPalException ex) when (ex.IsClientError)
                {
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 502);
                }

                var payment = order.Payment ?? new OrderPayment(orderId, currency);
                payment.SetAuthorized(authResult.PayPalOrderId, authResult.AuthorizationId, authResult.ExpiresAt);

                if (order.Payment == null)
                    await paymentRepo.AddAsync(payment, ct);
                else
                    await paymentRepo.UpdateAsync(payment, ct);

                return Results.Ok(new PayOrderResponse(
                    orderId,
                    authResult.PayPalOrderId,
                    authResult.AuthorizationId,
                    PaymentStatuses.Authorized,
                    amount));
            })
            .Produces<PayOrderResponse>()
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .Produces(422)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IRepository<Order> repo)
        => throw new System.NotImplementedException();
}

public record PayOrderRequest(CardPaymentDetails? Card, int? PaymentMethodId);
public record CardPaymentDetails(string Number, string Expiry, string SecurityCode, string? Name, string? CountryCode);
public record PayOrderResponse(int OrderId, string? PayPalOrderId, string? AuthorizationId, string Status, decimal Amount);
