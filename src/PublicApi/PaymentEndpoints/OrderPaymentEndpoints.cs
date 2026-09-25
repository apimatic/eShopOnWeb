using System;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class CallerIdentity
{
    public static string? BuyerId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
}

/// <summary>Places an order from catalog items for the signed-in shopper (awaiting payment).</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (request?.Items is null || request.Items.Count == 0)
                    return Results.BadRequest(new { message = "An order must contain at least one item." });

                var items = request.Items
                    .Select(i => new OrderLineInput { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
                    .ToList();
                var shipTo = new ShippingAddressInput
                {
                    Street = request.ShipTo.Street,
                    City = request.ShipTo.City,
                    State = request.ShipTo.State,
                    Country = request.ShipTo.Country,
                    ZipCode = request.ShipTo.ZipCode
                };

                var orderId = await service.PlaceOrderAsync(buyerId, items, shipTo, ct);
                return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>Authorizes (holds) the order total via a one-off card or a saved card.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var instruction = new PayInstruction
                {
                    Card = request?.Card?.ToCardDetails(),
                    SavedPaymentMethodId = request?.SavedPaymentMethodId
                };
                var view = await service.PayAsync(buyerId, orderId, instruction, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>Operator: fulfils the order and captures the held funds.</summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                var view = await service.FulfilAsync(orderId, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>Operator: cancels an order before fulfilment (releases the hold).</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                var view = await service.CancelAsync(orderId, ct);
                return Results.Ok(view);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>Refunds a captured payment (shopper-scoped), full or partial, idempotent per key.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { message = "A refund idempotencyKey is required." });

                var (refundId, payment) = await service.RefundAsync(buyerId, orderId, request.Amount, request.IdempotencyKey!, ct);
                return Results.Ok(new RefundOrderResponse { RefundId = refundId, Payment = payment });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>The caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                var orders = await service.GetMyOrdersAsync(buyerId, ct);
                return Results.Ok(orders);
            })
            .WithTags("PaymentEndpoints");
    }
}

/// <summary>Operator: reconciles PayPal's transaction record against eShop orders for a date range.</summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService service, CancellationToken ct) =>
            {
                if (to < from)
                    return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            })
            .WithTags("PaymentEndpoints");
    }
}
