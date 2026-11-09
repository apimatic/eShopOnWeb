using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using static BlazorShared.Authorization.Constants;

namespace Microsoft.eShopWeb.PublicApi.Stax;

internal static class EndpointHelpers
{
    public const string JWT = JwtBearerDefaults.AuthenticationScheme;

    public static string Shopper(HttpContext ctx) =>
        ShopperPayments.EmailOf(ctx.User) ?? throw new ApiException(401, "The token does not identify a shopper.");
}

/// <summary>Saved payment methods (shopper).</summary>
public class PaymentMethodEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (SavePaymentMethodRequest? request, HttpContext ctx, ShopperPayments payments, CancellationToken ct) =>
            {
                if (request == null) throw ApiException.BadRequest("A request body is required.");
                var dto = await payments.SaveAsync(EndpointHelpers.Shopper(ctx), request, ct);
                return Results.Created($"api/payment-methods/{dto.Id}", dto);
            }).WithTags("PaymentMethodEndpoints");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (HttpContext ctx, ShopperPayments payments, CancellationToken ct) =>
                Results.Ok(await payments.ListAsync(EndpointHelpers.Shopper(ctx), ct)))
            .WithTags("PaymentMethodEndpoints");

        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (string paymentMethodId, HttpContext ctx, ShopperPayments payments, CancellationToken ct) =>
                await payments.RemoveAsync(EndpointHelpers.Shopper(ctx), paymentMethodId, ct)
                    ? Results.NoContent()
                    : throw ApiException.NotFound("Payment method not found."))
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Orders, checkout, pay and status (shopper).</summary>
public class OrderEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (CreateOrderRequest? request, HttpContext ctx, OrderPayments orders, CancellationToken ct) =>
            {
                if (request == null) throw ApiException.BadRequest("A request body is required.");
                var dto = await orders.CreateOrderAsync(EndpointHelpers.Shopper(ctx), request, ct);
                return Results.Created($"api/orders/{dto.OrderId}", dto);
            }).WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId:int}/checkout",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (int orderId, PaymentMethodSelection? request, HttpContext ctx, OrderPayments orders, CancellationToken ct) =>
                Results.Ok(await orders.CheckoutAsync(EndpointHelpers.Shopper(ctx), orderId, request, ct)))
            .WithTags("OrderEndpoints");

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (int orderId, PaymentMethodSelection? request, HttpContext ctx, OrderPayments orders, CancellationToken ct) =>
                Results.Ok(await orders.PayAsync(EndpointHelpers.Shopper(ctx), orderId, request, ct)))
            .WithTags("OrderEndpoints");

        app.MapGet("api/orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (int orderId, HttpContext ctx, OrderPayments orders, CancellationToken ct) =>
                Results.Ok(await orders.GetOrderAsync(EndpointHelpers.Shopper(ctx), orderId, ct)))
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Close account (shopper).</summary>
public class AccountEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] async (HttpContext ctx, AccountCloser closer, CancellationToken ct) =>
            {
                await closer.CloseAsync(EndpointHelpers.Shopper(ctx), ct);
                return Results.NoContent();
            }).WithTags("AccountEndpoints");
    }
}

/// <summary>Staff (administrators) features.</summary>
public class AdminEndpoints : IEndpoint
{
    private const string ADMIN = Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (string email, ShopperPayments payments, CancellationToken ct) =>
            {
                if (!ShopperPayments.LooksLikeEmail(email)) throw ApiException.BadRequest("A valid shopper email is required.");
                return Results.Ok(await payments.ListEverAsync(email, ct));
            }).WithTags("AdminEndpoints");

        app.MapPost("api/admin/orders/{orderId:int}/refunds",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (int orderId, OrderPayments orders, CancellationToken ct) =>
                Results.Ok(await orders.RefundAsync(orderId, ct)))
            .WithTags("AdminEndpoints");

        app.MapGet("api/admin/orders/{orderId:int}",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (int orderId, OrderPayments orders, CancellationToken ct) =>
                Results.Ok(await orders.GetOrderForStaffAsync(orderId, ct)))
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (CreateInvoiceScheduleRequest? request, BackOffice office, CancellationToken ct) =>
            {
                if (request == null) throw ApiException.BadRequest("A request body is required.");
                var dto = await office.CreateScheduleAsync(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{dto.ScheduleId}", dto);
            }).WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (string scheduleId, BackOffice office, CancellationToken ct) =>
                Results.Ok(await office.PauseAsync(scheduleId, ct)))
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (string scheduleId, BackOffice office, CancellationToken ct) =>
                Results.Ok(await office.ResumeAsync(scheduleId, ct)))
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/sellers",
            [Authorize(Roles = ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] async (CreateSellerRequest? request, BackOffice office, CancellationToken ct) =>
            {
                if (request == null) throw ApiException.BadRequest("A request body is required.");
                var dto = await office.CreateSellerAsync(request, ct);
                return Results.Created($"api/admin/sellers/{dto.SellerId}", dto);
            }).WithTags("AdminEndpoints");
    }
}
