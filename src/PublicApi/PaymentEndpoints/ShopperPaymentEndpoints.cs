using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Shopper endpoints. The caller is identified by the JWT only.
/// </summary>
public class ShopperPaymentEndpoints : IEndpoint
{
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = Scheme)] async (SavePaymentMethodRequest request, ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                var saved = await payments.SavePaymentMethodAsync(email, request, ct);
                return Results.Created($"api/payment-methods/{saved.Id}", saved);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = Scheme)] async (ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                return Results.Ok(await payments.ListPaymentMethodsAsync(email, ct));
            })
            .WithTags("PaymentMethodEndpoints");

        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = Scheme)] async (string paymentMethodId, ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                await payments.DeletePaymentMethodAsync(email, paymentMethodId, ct);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");

        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = Scheme)] async (CreateOrderRequest request, ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                var order = await payments.CreateOrderAsync(email, request, ct);
                return Results.Created($"api/orders/{order.OrderId}", order);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");

        app.MapGet("api/orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = Scheme)] async (int orderId, ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                return Results.Ok(await payments.GetOrderAsync(email, orderId, ct));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId:int}/checkout",
            [Authorize(AuthenticationSchemes = Scheme)] async (int orderId, PayRequest request, ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                return Results.Ok(await payments.CheckoutAsync(email, orderId, request?.PaymentMethodId, ct));
            })
            .Produces<CheckoutResponse>()
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = Scheme)] async (int orderId, PayRequest request, ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                return Results.Ok(await payments.PayAsync(email, orderId, request?.PaymentMethodId, ct));
            })
            .Produces<PayResponse>()
            .WithTags("OrderEndpoints");

        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = Scheme)] async (ClaimsPrincipal user, PaymentService payments, CancellationToken ct) =>
            {
                var email = await payments.RequireShopperAsync(user);
                await payments.CloseAccountAsync(email, ct);
                return Results.NoContent();
            })
            .WithTags("AccountEndpoints");
    }
}
