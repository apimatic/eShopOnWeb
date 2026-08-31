using System;
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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized funds —
/// this is when the money is actually taken. A stale authorization is renewed
/// (reauthorized) first; one that can no longer be renewed fails with an
/// operator-actionable message. Idempotent: re-fulfilling returns the existing capture.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ILogger<FulfilOrderEndpoint> _logger;

    public FulfilOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway,
        ILogger<FulfilOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(new FulfilOrderRequest(orderId)))
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order is null)
        {
            return Results.NotFound(new { message = $"Order {request.OrderId} not found." });
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id));

        // Idempotent: fulfilling an already-fulfilled order returns the existing capture.
        if (order.Status == OrderStatus.Fulfilled && payment?.CaptureId is not null)
        {
            return OkResponse(request, order, payment, authorizationRenewed: false);
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment?.AuthorizationId is null)
        {
            return Results.Conflict(new
            {
                message = $"Order {order.Id} is {order.Status}; only an order with an authorized payment can be fulfilled."
            });
        }

        var renewed = false;
        try
        {
            var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId);

            // Renew a hold that has gone stale before attempting to capture it.
            if (authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow)
            {
                authorization = await Reauthorize(payment, order);
                renewed = true;
            }

            CaptureResult capture;
            try
            {
                capture = await _paymentGateway.CaptureAuthorizationAsync(payment.AuthorizationId,
                    payment.Amount, payment.Currency, $"eshop-order-{order.Id}-capture");
            }
            catch (PayPalApiException ex) when (!renewed && ex.StatusCode == 422)
            {
                // The hold went stale between our check and the capture; renew once and retry once.
                _logger.LogInformation("Capture for order {OrderId} failed with {Issue}; reauthorizing and retrying.",
                    order.Id, ex.Issue);
                await Reauthorize(payment, order);
                renewed = true;
                capture = await _paymentGateway.CaptureAuthorizationAsync(payment.AuthorizationId,
                    payment.Amount, payment.Currency, $"eshop-order-{order.Id}-capture");
            }

            payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount,
                capture.PayPalFee, capture.NetAmount);
            order.MarkFulfilled();

            await _paymentRepository.UpdateAsync(payment);
            await _orderRepository.UpdateAsync(order);

            return OkResponse(request, order, payment, renewed);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Fulfilment of order {OrderId} failed: {Error} {Issue} (debug {DebugId})",
                order.Id, ex.ErrorName, ex.Issue, ex.DebugId);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private async Task<AuthorizationDetails> Reauthorize(OrderPayment payment, Order order)
    {
        try
        {
            var authorization = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!,
                payment.Amount, payment.Currency, $"eshop-order-{order.Id}-reauthorize-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            payment.SetAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment);
            return authorization;
        }
        catch (PayPalApiException ex)
        {
            throw new PayPalApiException(ex.StatusCode, ex.ErrorName, ex.Issue, ex.DebugId,
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                $"(PayPal: {ex.ErrorName} {ex.Issue}). Cancel this order and ask the shopper to place and pay for a new one.");
        }
    }

    private static IResult OkResponse(FulfilOrderRequest request, Order order, OrderPayment payment, bool authorizationRenewed) =>
        Results.Ok(new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            Currency = payment.Currency,
            AuthorizationRenewed = authorizationRenewed
        });
}
