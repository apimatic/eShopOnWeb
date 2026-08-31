using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public bool AuthorizationRenewed { get; set; }
}

/// <summary>
/// Operator action: fulfils the order and captures the authorized funds.
/// A stale authorization is renewed (reauthorized) automatically; one that can no
/// longer be renewed returns a 409 with an operator-actionable message.
/// Idempotent: fulfilling an already-fulfilled order returns the existing capture.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             IRepository<Order> orderRepository,
             IRepository<Payment> paymentRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(orderId, orderRepository, paymentRepository, payPalClient);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId,
        IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IPayPalClient payPalClient)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound(new { message = $"Order {orderId} not found." });
        }

        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
        if (payment == null)
        {
            return Results.NotFound(new { message = $"No payment exists for order {orderId}." });
        }

        if (order.Status == OrderStatus.Fulfilled && payment.Status == PaymentStatus.Captured)
        {
            return Results.Ok(Map(order, payment, authorizationRenewed: false));
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment.AuthorizationId == null)
        {
            return Results.Conflict(new { message = $"Order {orderId} is in state {order.Status} and cannot be fulfilled." });
        }

        var renewed = false;

        // Renew the hold up-front if PayPal says it has already expired.
        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            var renewalError = await TryRenewAuthorizationAsync(payment, payPalClient);
            if (renewalError != null)
            {
                return renewalError;
            }
            renewed = true;
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency, $"capture-{payment.ClientToken:N}");
        }
        catch (PayPalApiException ex) when (!renewed && ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The hold went stale between our check and the capture: renew once and retry.
            var renewalError = await TryRenewAuthorizationAsync(payment, payPalClient);
            if (renewalError != null)
            {
                return renewalError;
            }
            renewed = true;

            capture = await payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency, $"capture-{payment.ClientToken:N}-r");
        }

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await paymentRepository.UpdateAsync(payment);
        await orderRepository.UpdateAsync(order);

        return Results.Ok(Map(order, payment, renewed));
    }

    private static async Task<IResult?> TryRenewAuthorizationAsync(Payment payment, IPayPalClient payPalClient)
    {
        try
        {
            var renewed = await payPalClient.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency,
                $"reauthorize-{payment.ClientToken:N}-{Guid.NewGuid():N}");

            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            return null;
        }
        catch (PayPalApiException ex)
        {
            return Results.Conflict(new
            {
                message = $"The PayPal authorization for payment {payment.Id} has expired and can no longer be renewed " +
                          $"({ex.ErrorName ?? "renewal rejected"}). Ask the shopper to pay again or cancel the order.",
                payPalDebugId = ex.DebugId
            });
        }
    }

    private static FulfilOrderResponse Map(Order order, Payment payment, bool authorizationRenewed) => new FulfilOrderResponse
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.Status.ToString(),
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        Currency = payment.Currency,
        CapturedAt = payment.CapturedAt,
        AuthorizationRenewed = authorizationRenewed
    };
}
