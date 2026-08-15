using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentEndpointHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentAuth
{
    public const string AdminOnly = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string Jwt = JwtBearerDefaults.AuthenticationScheme;
}

/// <summary>POST /api/orders — place an order awaiting payment (shopper).</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                PayPalSettings settings,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);
                var lines = (request.Items ?? new List<OrderLineDto>())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var orderId = await service.PlaceOrderAsync(buyerId, lines, request.ShipToAddress.ToShippingAddress(), ct);
                var order = await service.GetOrderForBuyerAsync(buyerId, orderId, ct);

                return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse
                {
                    OrderId = orderId,
                    Status = order.Order.Status.ToString(),
                    Total = order.Order.Total(),
                    Currency = string.IsNullOrWhiteSpace(settings.Currency) ? "USD" : settings.Currency
                });
            }))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }
}

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total (shopper).</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                int orderId,
                PayOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);

                PaymentInstrument instrument;
                if (request.SavedPaymentMethodId is { } savedId)
                {
                    instrument = PaymentInstrument.FromSavedCard(savedId);
                }
                else if (request.Card is not null)
                {
                    instrument = PaymentInstrument.FromCard(request.Card.ToDomain());
                }
                else
                {
                    return Results.Json(
                        new PaymentProblem(StatusCodes.Status400BadRequest, "Provide either card details or a saved payment method id.", null, null),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var payment = await service.AuthorizeAsync(buyerId, orderId, instrument, ct);
                return Results.Ok(new PayOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentStateDto.From(payment)!
                });
            }))
            .Produces<PayOrderResponse>()
            .WithTags("Payments");
    }
}

/// <summary>POST /api/orders/{orderId}/fulfil — capture the held funds (operator).</summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = PaymentAuth.AdminOnly, AuthenticationSchemes = PaymentAuth.Jwt)] async (
                int orderId,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var payment = await service.FulfilAsync(orderId, ct);
                return Results.Ok(new PayOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentStateDto.From(payment)!
                });
            }))
            .Produces<PayOrderResponse>()
            .WithTags("Payments");
    }
}

/// <summary>POST /api/orders/{orderId}/cancel — release the hold before fulfilment (operator).</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = PaymentAuth.AdminOnly, AuthenticationSchemes = PaymentAuth.Jwt)] async (
                int orderId,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var payment = await service.CancelAsync(orderId, ct);
                return Results.Ok(new PayOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentStateDto.From(payment) ?? new PaymentStateDto { Status = "Cancelled" }
                });
            }))
            .Produces<PayOrderResponse>()
            .WithTags("Payments");
    }
}

/// <summary>POST /api/orders/{orderId}/refunds — refund a captured order, full or partial (shopper).</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                int orderId,
                RefundOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    return Results.Json(
                        new PaymentProblem(StatusCodes.Status400BadRequest, "An idempotencyKey is required for a refund.", null, null),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var refund = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);
                var order = await service.GetOrderForBuyerAsync(buyerId, orderId, ct);

                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", new RefundResponse
                {
                    RefundId = refund.Id,
                    PayPalRefundId = refund.PayPalRefundId,
                    Amount = refund.Amount,
                    Status = refund.Status,
                    Payment = PaymentStateDto.From(order.Payment)!
                });
            }))
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }
}

/// <summary>GET /api/my-orders — the caller's orders with their payment state (shopper).</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = PaymentAuth.Jwt)] async (
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                var buyerId = GetBuyerId(user);
                var orders = await service.GetOrdersForBuyerAsync(buyerId, ct);
                var dtos = orders.Select(o => OrderSummaryDto.From(o.Order, o.Payment)).ToList();
                return Results.Ok(dtos);
            }))
            .Produces<List<OrderSummaryDto>>()
            .WithTags("Payments");
    }
}

/// <summary>GET /api/reconciliation?from=&amp;to= — reconcile PayPal transactions against eShop orders (operator).</summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = PaymentAuth.AdminOnly, AuthenticationSchemes = PaymentAuth.Jwt)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                IOrderPaymentService service,
                CancellationToken ct) =>
            await Execute(async () =>
            {
                if (to < from)
                {
                    return Results.Json(
                        new PaymentProblem(StatusCodes.Status400BadRequest, "'to' must not be earlier than 'from'.", null, null),
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            }))
            .Produces<ReconciliationReport>()
            .WithTags("Payments");
    }
}
