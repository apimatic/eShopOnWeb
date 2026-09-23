using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using Roles = BlazorShared.Authorization.Constants.Roles;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders — place an order from catalog items (shopper). Returns the new order id.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService payments, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                var lines = request.Items.Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity)).ToList();
                var shipTo = request.ShipToAddress is null ? null
                    : new AddressInput(request.ShipToAddress.Street, request.ShipToAddress.City,
                        request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

                var orderId = await payments.PlaceOrderAsync(buyerId, lines, shipTo, ct);
                return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total (shopper).</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService payments, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                var pay = new PayInput(request.Card?.ToCardDetails(), request.SavedPaymentMethodId);
                var view = await payments.AuthorizeAsync(buyerId, orderId, pay, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>POST /api/orders/{orderId}/fulfil — capture the money (operator/admin).</summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService payments, CancellationToken ct) =>
            {
                var view = await payments.FulfilAsync(orderId, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>POST /api/orders/{orderId}/cancel — release the hold before fulfilment (operator/admin).</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService payments, CancellationToken ct) =>
            {
                var view = await payments.CancelAsync(orderId, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>POST /api/orders/{orderId}/refunds — refund a captured payment, full or partial (shopper).</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundRequestDto request, ClaimsPrincipal user, IPaymentService payments, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                var result = await payments.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey, ct);
                return Results.Ok(new RefundResponse
                {
                    RefundId = result.RefundId,
                    Status = result.Status,
                    Amount = result.Amount,
                    Currency = result.Currency,
                });
            })
            .Produces<RefundResponse>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>GET /api/my-orders — the caller's orders with their payment state (shopper).</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IPaymentService payments, CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                var orders = await payments.GetMyOrdersAsync(buyerId, ct);
                return Results.Ok(orders);
            })
            .Produces<System.Collections.Generic.IReadOnlyList<PaymentView>>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>GET /api/reconciliation?from=&amp;to= — PayPal transactions lined up against eShop orders (operator/admin).</summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async ([FromQuery(Name = "from")] DateTimeOffset fromDate, [FromQuery(Name = "to")] DateTimeOffset toDate,
                   IPaymentService payments, CancellationToken ct) =>
            {
                var report = await payments.ReconcileAsync(fromDate, toDate, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }
}
