using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; this is where the money
/// is actually taken (the authorization is captured). A hold that has gone stale before fulfilment is
/// renewed rather than failing the fulfilment; one that can no longer be renewed reports an
/// operator-actionable error. Idempotent: re-fulfilling a paid order returns its current state.
/// Administrator only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    // Original 3-day honor period + the window in which PayPal still allows re-authorization.
    private static readonly TimeSpan RenewableWindow = TimeSpan.FromDays(30);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IRepository<Order> orderRepository,
                IPaymentProcessor processor,
                CancellationToken ct) =>
            {
                var order = await orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
                if (order is null)
                {
                    return Results.NotFound(new { message = $"Order {orderId} was not found." });
                }

                if (order.PaymentStatus == OrderPaymentStatus.Paid)
                {
                    return Results.Ok(PaymentMapping.ToOrderPaymentResponse(order));
                }

                if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment?.AuthorizationId is null)
                {
                    return Results.Conflict(new { message = $"Order {orderId} must be authorized before it can be fulfilled (current state: {order.PaymentStatus})." });
                }

                var payment = order.Payment;
                var amount = order.Total();
                var authorizationId = payment.AuthorizationId!;

                var stale = payment.AuthorizationExpiresAt is DateTimeOffset expiresAt && DateTimeOffset.UtcNow >= expiresAt;
                if (stale)
                {
                    if (!IsRenewable(payment.AuthorizedAt))
                    {
                        throw new AuthorizationNotRenewableException(
                            "The payment hold for this order has expired and can no longer be renewed. Ask the shopper to place and pay for a new order.");
                    }

                    var snapshot = await processor.ReauthorizeAsync(authorizationId, amount, $"reauth-{order.Id}", ct);
                    order.RecordReauthorization(snapshot.AuthorizationId, snapshot.Status, snapshot.ExpiresAt);
                    await orderRepository.UpdateAsync(order, ct);
                    authorizationId = snapshot.AuthorizationId;
                }

                CaptureResult capture;
                try
                {
                    capture = await processor.CaptureAsync(authorizationId, amount, $"capture-{order.Id}", ct);
                }
                catch (PaymentProcessorException ex) when (!stale && ex.StatusCode is 409 or 422)
                {
                    // The hold may have gone stale between check and capture — renew once and retry.
                    if (!IsRenewable(payment.AuthorizedAt))
                    {
                        throw new AuthorizationNotRenewableException(
                            "The payment hold could not be captured and can no longer be renewed. Ask the shopper to place and pay for a new order.", ex);
                    }

                    var snapshot = await processor.ReauthorizeAsync(authorizationId, amount, $"reauth-{order.Id}", ct);
                    order.RecordReauthorization(snapshot.AuthorizationId, snapshot.Status, snapshot.ExpiresAt);
                    await orderRepository.UpdateAsync(order, ct);

                    capture = await processor.CaptureAsync(snapshot.AuthorizationId, amount, $"capture-{order.Id}-retry", ct);
                }

                order.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount,
                    capture.PayPalFee, capture.NetAmount, DateTimeOffset.UtcNow);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(PaymentMapping.ToOrderPaymentResponse(order));
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    private static bool IsRenewable(DateTimeOffset? authorizedAt) =>
        authorizedAt is not DateTimeOffset at || DateTimeOffset.UtcNow < at.Add(RenewableWindow);
}
