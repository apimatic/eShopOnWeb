using System;
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
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class ShopperAuth
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;
}

/// <summary>Saves a card or a bank account for the caller.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (JsonElement request, ClaimsPrincipal user,
                PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                var saved = await payments.SavePaymentMethod(email, request, ct);
                return Results.Json(saved, statusCode: StatusCodes.Status201Created);
            }))
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Lists the caller's saved cards and bank accounts.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (ClaimsPrincipal user, PaymentsService payments,
                CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                return Results.Ok(await payments.ListPaymentMethods(email, ct));
            }))
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Removes one of the caller's saved payment methods.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (string paymentMethodId, ClaimsPrincipal user,
                PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                await payments.RemovePaymentMethod(email, ApiProblem.ParseId(paymentMethodId, "payment method"), ct);
                return Results.Ok(new { id = paymentMethodId, removed = true });
            }))
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Places an order for catalog items.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (JsonElement request, ClaimsPrincipal user,
                PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                var order = await payments.PlaceOrder(email, request, ct);
                return Results.Json(PaymentsService.OrderDto(order), statusCode: StatusCodes.Status201Created);
            }))
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows what paying with a given payment method costs, fee included.</summary>
public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (string orderId, JsonElement request,
                ClaimsPrincipal user, PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                var pmId = PaymentMethodIdOf(request);
                var (order, quote) = await payments.Checkout(email, ApiProblem.ParseId(orderId, "order"), pmId, ct);
                return Results.Ok(new
                {
                    orderId = order.Id,
                    paymentMethodId = pmId,
                    paymentMethod = quote.Method,
                    amount = quote.Amount,
                    subtotal = quote.Amount,
                    fee = quote.Fee,
                    total = quote.Total,
                    currency = order.Currency
                });
            }))
            .WithTags("OrderEndpoints");
    }

    internal static Guid PaymentMethodIdOf(JsonElement request)
    {
        foreach (var name in new[] { "paymentMethodId", "payment_method_id", "paymentMethod", "payment_method", "id" })
        {
            var text = request.ValueKind == JsonValueKind.Object ? request.Str(name) : null;
            if (text is not null)
                return Guid.TryParse(text, out var id) ? id : throw ApiProblem.BadRequest("paymentMethodId is not a valid id.");
        }
        throw ApiProblem.BadRequest("paymentMethodId is required.");
    }
}

/// <summary>Pays an order with a saved card or bank account.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (string orderId, JsonElement request,
                ClaimsPrincipal user, PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                var pmId = CheckoutEndpoint.PaymentMethodIdOf(request);
                var order = await payments.Pay(email, ApiProblem.ParseId(orderId, "order"), pmId, ct);
                return Results.Ok(PaymentsService.OrderDto(order));
            }))
            .WithTags("OrderEndpoints");
    }
}

/// <summary>The caller's view of an order.</summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (string orderId, ClaimsPrincipal user,
                PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                var order = await payments.OwnedOrder(email, ApiProblem.ParseId(orderId, "order"), ct);
                try
                {
                    await payments.SyncPayment(order, ct);
                }
                catch (Exception ex) when (ApiProblem.IsStaxFailure(ex))
                {
                    // Stax is unreachable right now; the background sync will catch up.
                }
                return Results.Ok(PaymentsService.OrderDto(order));
            }))
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Closes the caller's account and erases their data, at Stax and in the shop.</summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = ShopperAuth.SCHEME)] (ClaimsPrincipal user, PaymentsService payments,
                CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var email = await payments.RequireShopper(user);
                await payments.CloseAccount(email, ct);
                return Results.Ok(new { closed = true });
            }))
            .WithTags("AccountEndpoints");
    }
}
