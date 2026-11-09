using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Stax;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Saved payment methods (shopper).</summary>
public class PaymentMethodEndpoints : IEndpoint
{
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = Scheme)] async (JsonElement body, ClaimsPrincipal user,
                ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var saved = await methods.AddAsync(email, LooseBody.From(body), ct);
                return Results.Created($"api/payment-methods/{saved.Id}", PaymentMethodService.ToDto(saved));
            })
            .WithTags("PaymentMethodEndpoints");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = Scheme)] async (ClaimsPrincipal user,
                ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var list = await methods.ListAsync(email, ct);
                return Results.Ok(list.Select(PaymentMethodService.ToDto));
            })
            .WithTags("PaymentMethodEndpoints");

        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = Scheme)] async (string paymentMethodId, ClaimsPrincipal user,
                ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var removed = await methods.RemoveAsync(email, paymentMethodId, ct);
                return Results.Ok(new { paymentMethodId = removed.Id, removedAt = removed.RemovedAt });
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Orders, checkout, pay and order status (shopper).</summary>
public class OrderEndpoints : IEndpoint
{
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = Scheme)] async (JsonElement body, ClaimsPrincipal user,
                ShopperService shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var lines = ParseLines(body);
                var order = await orders.CreateOrderAsync(email, lines, ct);
                return Results.Created($"api/orders/{order.Id}", OrderPaymentService.ToShopperDto(order));
            })
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId:int}/checkout",
            [Authorize(AuthenticationSchemes = Scheme)] async (int orderId, JsonElement body, ClaimsPrincipal user,
                ShopperService shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var pmId = PaymentMethodIdOf(body);
                var (order, pm, quote) = await orders.CheckoutAsync(email, orderId, pmId, ct);
                return Results.Ok(new
                {
                    orderId = order.Id,
                    paymentMethodId = pm.Id,
                    paymentMethodType = pm.Method,
                    subtotal = quote.Subtotal,
                    fee = quote.Fee,
                    total = quote.Total,
                    currency = "USD",
                });
            })
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = Scheme)] async (int orderId, JsonElement body, ClaimsPrincipal user,
                ShopperService shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var order = await orders.PayAsync(email, orderId, PaymentMethodIdOf(body), ct);
                return Results.Ok(OrderPaymentService.ToShopperDto(order));
            })
            .WithTags("OrderEndpoints");

        app.MapGet("api/orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = Scheme)] async (int orderId, ClaimsPrincipal user,
                ShopperService shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                var order = await orders.GetOwnedOrderAsync(email, orderId, ct);
                await orders.SyncPaymentAsync(order, ct);
                return Results.Ok(OrderPaymentService.ToShopperDto(order));
            })
            .WithTags("OrderEndpoints");
    }

    private static string PaymentMethodIdOf(JsonElement body)
        => LooseBody.From(body).Str("paymentMethodId", "paymentMethod", "paymentMethodID", "id")
           ?? throw ApiException.BadRequest("'paymentMethodId' is required.");

    private static System.Collections.Generic.List<(int, int)> ParseLines(JsonElement body)
    {
        var root = LooseBody.From(body);
        var raw = root.Raw("items", "lines", "orderItems");
        if (raw == null || raw.Value.ValueKind != JsonValueKind.Array)
            throw ApiException.BadRequest("'items' must be a list of catalog items with quantities.");

        var lines = new System.Collections.Generic.List<(int, int)>();
        foreach (var el in raw.Value.EnumerateArray())
        {
            var line = LooseBody.From(el);
            var itemId = line.Int("catalogItemId", "itemId", "productId", "id")
                ?? throw ApiException.BadRequest("Each item needs a 'catalogItemId'.");
            var qty = line.Int("quantity", "units", "qty") ?? 1;
            lines.Add((itemId, qty));
        }
        return lines;
    }
}

/// <summary>Closing a shopper account.</summary>
public class AccountEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ClaimsPrincipal user,
                ShopperService shoppers, AccountService accounts, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(user, ct);
                await accounts.CloseAsync(email, ct);
                return Results.Ok(new { closed = true });
            })
            .WithTags("AccountEndpoints");
    }
}
