using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

internal static class EndpointHelpers
{
    public const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string JWT = JwtBearerDefaults.AuthenticationScheme;

    public static async Task<Body> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength == 0 || (request.ContentLength == null && !request.HasJsonContentType()))
            return new Body(null);
        try
        {
            var doc = await JsonDocument.ParseAsync(request.Body);
            return new Body(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            throw ApiException.BadRequest("The request body is not valid JSON.");
        }
    }

    public static string? PaymentMethodId(Body body) =>
        body.Str("paymentMethodId", "paymentMethod", "methodId", "payment_method_id")
        ?? body.Child("paymentMethod")?.Str("id");

    public static async Task<IResult> Run(System.Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ApiException ex) { return Results.Json(new { statusCode = ex.StatusCode, message = ex.Message }, statusCode: ex.StatusCode); }
    }
}

// ----------------------------------------------------------- saved payment methods

public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (HttpRequest req, ClaimsPrincipal user,
                ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    var body = await EndpointHelpers.ReadBodyAsync(req);
                    var created = await methods.CreateAsync(email, body, ct);
                    return Results.Json(created, statusCode: 201);
                }))
            .WithTags("PaymentMethodEndpoints");
}

public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (ClaimsPrincipal user,
                ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    return Results.Ok(new { paymentMethods = await methods.ListAsync(email, ct) });
                }))
            .WithTags("PaymentMethodEndpoints");
}

public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (string paymentMethodId, ClaimsPrincipal user,
                ShopperService shoppers, PaymentMethodService methods, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    await methods.RemoveAsync(email, paymentMethodId, ct);
                    return Results.NoContent();
                }))
            .WithTags("PaymentMethodEndpoints");
}

// ------------------------------------------------------------------------- orders

public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (HttpRequest req, ClaimsPrincipal user,
                ShopperService shoppers, ShopOrderService orders, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    var body = await EndpointHelpers.ReadBodyAsync(req);
                    var order = await orders.PlaceAsync(email, body, ct);
                    return Results.Created($"api/orders/{order.Id}", ShopOrderService.MapForShopper(order));
                }))
            .WithTags("OrderEndpoints");
}

public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (int orderId, ClaimsPrincipal user,
                ShopperService shoppers, ShopOrderService orders, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    var order = await orders.GetOwnedAsync(email, orderId, ct);
                    // A bank payment settles days later: check with Stax rather than trusting a stale state.
                    try { await orders.RefreshPaymentAsync(order, ct); } catch (StaxException) { }
                    return Results.Ok(ShopOrderService.MapForShopper(order));
                }))
            .WithTags("OrderEndpoints");
}

public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (int orderId, HttpRequest req, ClaimsPrincipal user,
                ShopperService shoppers, ShopOrderService orders, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    var body = await EndpointHelpers.ReadBodyAsync(req);
                    var q = await orders.CheckoutAsync(email, orderId, EndpointHelpers.PaymentMethodId(body), ct);
                    return Results.Ok(new
                    {
                        orderId,
                        paymentMethodId = q.PaymentMethodId,
                        paymentMethod = q.Method,
                        subtotal = q.Subtotal,
                        fee = q.Fee,
                        total = q.Total
                    });
                }))
            .WithTags("OrderEndpoints");
}

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (int orderId, HttpRequest req, ClaimsPrincipal user,
                ShopperService shoppers, ShopOrderService orders, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    var body = await EndpointHelpers.ReadBodyAsync(req);
                    var order = await orders.PayAsync(email, orderId, EndpointHelpers.PaymentMethodId(body),
                        body.Decimal("expectedTotal", "total"), ct);
                    return Results.Ok(ShopOrderService.MapForShopper(order));
                }))
            .WithTags("OrderEndpoints");
}

// ---------------------------------------------------------------------- account

public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = EndpointHelpers.JWT)] (ClaimsPrincipal user,
                ShopperService shoppers, AccountService accounts, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var email = await shoppers.RequireShopperAsync(user);
                    await accounts.CloseAsync(user.Identity!.Name!, email, ct);
                    return Results.NoContent();
                }))
            .WithTags("AccountEndpoints");
}

// ------------------------------------------------------------------- staff area

public class SupportPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (string email,
                PaymentMethodService methods, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                    Results.Ok(new { email, paymentMethods = await methods.ListForSupportAsync(email, ct) })))
            .WithTags("AdminEndpoints");
}

public class AdminGetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (int orderId,
                ShopOrderService orders, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var order = await orders.GetAnyAsync(orderId, ct);
                    try { await orders.RefreshPaymentAsync(order, ct); } catch (StaxException) { }
                    return Results.Ok(ShopOrderService.MapForStaff(order));
                }))
            .WithTags("AdminEndpoints");
}

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (int orderId,
                ShopOrderService orders, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var order = await orders.RefundAsync(orderId, ct);
                    var staff = ShopOrderService.MapForStaff(order);
                    // 201 when done, 202 when the refund waits for Stax (e.g. a bank payment that is too fresh).
                    return Results.Json(staff, statusCode: order.RefundStatus == RefundState.REFUNDED ? 201 : 202);
                }))
            .WithTags("AdminEndpoints");
}

public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (HttpRequest req,
                InvoiceScheduleService schedules, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var body = await EndpointHelpers.ReadBodyAsync(req);
                    return Results.Json(await schedules.CreateAsync(body, ct), statusCode: 201);
                }))
            .WithTags("AdminEndpoints");
}

public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (string scheduleId,
                InvoiceScheduleService schedules, CancellationToken ct) =>
                EndpointHelpers.Run(async () => Results.Ok(await schedules.PauseAsync(scheduleId, ct))))
            .WithTags("AdminEndpoints");
}

public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (string scheduleId,
                InvoiceScheduleService schedules, CancellationToken ct) =>
                EndpointHelpers.Run(async () => Results.Ok(await schedules.ResumeAsync(scheduleId, ct))))
            .WithTags("AdminEndpoints");
}

public class OnboardSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app) =>
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = EndpointHelpers.ADMIN, AuthenticationSchemes = EndpointHelpers.JWT)] (HttpRequest req,
                SellerService sellers, CancellationToken ct) =>
                EndpointHelpers.Run(async () =>
                {
                    var body = await EndpointHelpers.ReadBodyAsync(req);
                    return Results.Json(await sellers.OnboardAsync(body, ct), statusCode: 201);
                }))
            .WithTags("AdminEndpoints");
}
