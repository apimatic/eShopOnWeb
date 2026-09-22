using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// HTTP surface for the pay-for-an-order flow, routed under <c>/api/</c> on PublicApi (JWT auth).
/// Each action is separately invocable. Shopper actions act only on the caller's own data; fulfil,
/// cancel and reconciliation are restricted to the administrator role.
/// </summary>
public static class OrderPaymentEndpoints
{
    private const string AdminRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    // The host uses the Identity cookie scheme as its default challenge, so JWT endpoints must name
    // the bearer scheme explicitly (matching the project's existing endpoints).
    private static AuthorizeAttribute ShopperAuth() =>
        new() { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme };

    private static AuthorizeAttribute AdminAuth() =>
        new() { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = AdminRole };

    public static IEndpointRouteBuilder MapOrderPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // Place an order from catalog items; starts awaiting payment.
        app.MapPost("api/orders", async (
                PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService payments, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var lines = (request.Items ?? new())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();
                var payment = await payments.PlaceOrderAsync(buyerId, lines, request.ShipToAddress.ToAddress(), ct);
                var response = new PlaceOrderResponse
                {
                    OrderId = payment.OrderId,
                    State = payment.State.ToString(),
                    Total = payment.Amount,
                    Currency = payment.Currency
                };
                return Results.Created($"api/orders/{payment.OrderId}", response);
            })
            .RequireAuthorization(ShopperAuth())
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");

        // Authorize (hold) the order total.
        app.MapPost("api/orders/{orderId:int}/pay", async (
                int orderId, PayOrderRequest request, ClaimsPrincipal user,
                IPaymentService payments, CancellationToken ct) =>
            {
                var instruction = new PayInstruction
                {
                    Card = request.Card?.ToCardDetails(),
                    SavedPaymentMethodId = request.SavedPaymentMethodId
                };
                var payment = await payments.PayAsync(orderId, user.GetBuyerId(), instruction, ct);
                return Results.Ok(payment.ToView());
            })
            .RequireAuthorization(ShopperAuth())
            .Produces<OrderPaymentView>()
            .WithTags("Payments");

        // Operator: fulfil the order (capture the payment).
        app.MapPost("api/orders/{orderId:int}/fulfil", async (
                int orderId, IPaymentService payments, CancellationToken ct) =>
            {
                var payment = await payments.FulfilAsync(orderId, ct);
                return Results.Ok(payment.ToView());
            })
            .RequireAuthorization(AdminAuth())
            .Produces<OrderPaymentView>()
            .WithTags("Payments");

        // Operator: cancel before fulfilment (release held funds).
        app.MapPost("api/orders/{orderId:int}/cancel", async (
                int orderId, IPaymentService payments, CancellationToken ct) =>
            {
                var payment = await payments.CancelAsync(orderId, ct);
                return Results.Ok(payment.ToView());
            })
            .RequireAuthorization(AdminAuth())
            .Produces<OrderPaymentView>()
            .WithTags("Payments");

        // Refund a captured payment (full or partial) for the caller's own order.
        app.MapPost("api/orders/{orderId:int}/refunds", async (
                int orderId, RefundRequestDto request, ClaimsPrincipal user,
                IPaymentService payments, CancellationToken ct) =>
            {
                var (payment, refund) = await payments.RefundAsync(
                    orderId, user.GetBuyerId(), request.Amount, request.IdempotencyKey ?? string.Empty, ct);
                var response = new RefundResponseDto
                {
                    RefundId = refund.PayPalRefundId ?? string.Empty,
                    OrderId = orderId,
                    Amount = refund.Amount,
                    Status = refund.Status,
                    State = payment.State.ToString()
                };
                return Results.Created($"api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
            })
            .RequireAuthorization(ShopperAuth())
            .Produces<RefundResponseDto>(StatusCodes.Status201Created)
            .WithTags("Payments");

        // The caller's orders with their payment state.
        app.MapGet("api/my-orders", async (
                ClaimsPrincipal user, IPaymentService payments, CancellationToken ct) =>
            {
                var results = await payments.GetOrdersForBuyerAsync(user.GetBuyerId(), ct);
                var response = new MyOrdersResponse
                {
                    Orders = results.Select(r => new MyOrderView
                    {
                        OrderId = r.Order.Id,
                        OrderDate = r.Order.OrderDate,
                        Total = r.Order.Total(),
                        Payment = r.Payment?.ToView()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .RequireAuthorization(ShopperAuth())
            .Produces<MyOrdersResponse>()
            .WithTags("Payments");

        // Operator: reconcile PayPal's transaction record against eShop orders for a date range.
        app.MapGet("api/reconciliation", async (
                DateTimeOffset from, DateTimeOffset to,
                IReconciliationService reconciliation, CancellationToken ct) =>
            {
                var report = await reconciliation.BuildAsync(from, to, ct);
                return Results.Ok(report);
            })
            .RequireAuthorization(AdminAuth())
            .Produces<ReconciliationReport>()
            .WithTags("Payments");

        return app;
    }
}
