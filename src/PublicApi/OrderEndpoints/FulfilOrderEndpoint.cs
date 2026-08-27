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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils the order and captures the authorized money. A stale
/// authorization is renewed first; one that can no longer be renewed fails with an
/// operator-actionable conflict. Idempotent: re-fulfilling returns the existing capture.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int>
{
    // Renew the hold when it expires within this safety margin.
    private static readonly TimeSpan StalenessMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;

    public FulfilOrderEndpoint(IRepository<Order> orderRepository, IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId) => HandleAsync(orderId, CancellationToken.None);

    private async Task<IResult> HandleAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        if (order is null)
        {
            return Results.NotFound();
        }

        var response = new FulfilOrderResponse { OrderId = order.Id };

        // Idempotent replay: already captured, return what PayPal reported.
        if (order.Status == OrderStatus.Fulfilled)
        {
            response.Status = order.Status.ToString();
            response.Payment = PaymentDto.FromOrder(order);
            return Results.Ok(response);
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.AuthorizationId is null)
        {
            throw new PaymentStateException($"Order {order.Id} is {order.Status}; only a paid (authorized) order can be fulfilled.");
        }

        var authorizationId = order.AuthorizationId;
        var authorization = await _paymentGateway.GetAuthorizationAsync(authorizationId, ct);

        switch (authorization.Status)
        {
            case "CREATED":
            case "PENDING":
                break;
            case "CAPTURED":
            case "PARTIALLY_CAPTURED":
                throw new PaymentStateException(
                    $"PayPal reports authorization {authorizationId} as {authorization.Status}, but order {order.Id} " +
                    "has no capture recorded. Reconcile the order against PayPal before fulfilling.");
            default:
                throw new PaymentStateException(
                    $"PayPal reports authorization {authorizationId} as {authorization.Status}; order {order.Id} cannot be fulfilled. " +
                    "Cancel the order and ask the shopper to pay again.");
        }

        if (authorization.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow + StalenessMargin)
        {
            // The hold has gone stale: renew it rather than failing the fulfilment.
            // A 422 from PayPal here surfaces as an operator-actionable PaymentStateException.
            var renewed = await _paymentGateway.ReauthorizeAsync(
                authorizationId, order.Total(), order.Currency ?? authorization.Currency ?? "USD",
                PaymentKeys.ReauthorizeKey(order.Id), ct);
            order.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            authorizationId = renewed.AuthorizationId;
        }

        var capture = await _paymentGateway.CaptureAuthorizationAsync(
            authorizationId, PaymentKeys.CaptureKey(order.Id), ct);

        order.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);

        response.Status = order.Status.ToString();
        response.Payment = PaymentDto.FromOrder(order);
        return Results.Ok(response);
    }
}
