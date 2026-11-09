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

// Shopper endpoints: any signed-in account; the caller is always taken from the JWT.
// Staff endpoints: the Administrators role, as elsewhere in this project.

internal static class Auth
{
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;
    public const string ADMIN = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
}

/// <summary>Saves a card or bank account for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (SavePaymentMethodRequest request, HttpContext http,
                ShopperContext shoppers, PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                var dto = await methods.SaveAsync(email, request, ct);
                return Results.Created($"api/payment-methods/{dto.Id}", dto);
            })
            .Produces<PaymentMethodDto>(201)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Lists the signed-in shopper's saved payment methods.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (HttpContext http, ShopperContext shoppers,
                PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                return Results.Ok(await methods.ListAsync(email, ct));
            })
            .Produces<PaymentMethodDto[]>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Removes one of the signed-in shopper's saved payment methods.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (string paymentMethodId, HttpContext http,
                ShopperContext shoppers, PaymentMethodService methods, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                await methods.RemoveAsync(email, paymentMethodId, ct);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Places an order for catalog items.</summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (CreateOrderRequest request, HttpContext http,
                ShopperContext shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                var order = await orders.CreateOrderAsync(email, request, ct);
                return Results.Created($"api/orders/{order.Id}", OrderPaymentService.ToDto(order, admin: false));
            })
            .Produces<OrderDto>(201)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows the exact amount (fee and total) the shopper will pay with the chosen payment method.</summary>
public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (int orderId, PaymentMethodChoiceRequest request, HttpContext http,
                ShopperContext shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                return Results.Ok(await orders.CheckoutAsync(email, orderId, request.PaymentMethodId, ct));
            })
            .Produces<CheckoutDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Pays an order with a saved card or bank account.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (int orderId, PaymentMethodChoiceRequest request, HttpContext http,
                ShopperContext shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                var order = await orders.PayAsync(email, orderId, request.PaymentMethodId, ct);
                return Results.Ok(OrderPaymentService.ToDto(order, admin: false));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>The shopper's view of one of their orders.</summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (int orderId, HttpContext http,
                ShopperContext shoppers, OrderPaymentService orders, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                var order = await orders.GetOwnedOrderAsync(email, orderId, ct);
                await TryRefresh(orders, order, ct);
                return Results.Ok(OrderPaymentService.ToDto(order, admin: false));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    internal static async Task TryRefresh(OrderPaymentService orders, ApplicationCore.Entities.Payments.ShopperOrder order, CancellationToken ct)
    {
        try { await orders.RefreshAsync(order, ct); }
        catch (StaxException) { /* show what is known; the background reconciler keeps trying */ }
    }
}

/// <summary>Closes the signed-in shopper's account and erases their data, at Stax and in the shop.</summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = Auth.SCHEME)] async (HttpContext http, ShopperContext shoppers,
                AccountService accounts, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopperAsync(http.User);
                await accounts.CloseAsync(email, ct);
                return Results.NoContent();
            })
            .WithTags("AccountEndpoints");
    }
}

/// <summary>Support: every card and bank account a shopper ever saved, removed ones included.</summary>
public class AdminShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (string email, ShopperContext shoppers,
                PaymentMethodService methods, CancellationToken ct) =>
            {
                if (!ShopperContext.IsValidEmail(email)) throw new ApiRequestException(400, "A valid shopper email is required.");
                email = ShopperContext.Normalize(email);
                var list = await methods.ListEverythingAsync(email, ct);
                if (list.Count == 0 && await shoppers.FindUserAsync(email) is null)
                    throw new ApiRequestException(404, "Shopper not found.");
                return Results.Ok(list);
            })
            .Produces<PaymentMethodDto[]>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff refund the whole amount of a paid order, by card or by bank account.</summary>
public class AdminRefundEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (int orderId, OrderPaymentService orders, CancellationToken ct) =>
            {
                var order = await orders.RefundAsync(orderId, ct);
                var dto = OrderPaymentService.ToDto(order, admin: true);
                return Results.Json(new { orderId = dto.OrderId, refundStatus = dto.RefundStatus, refundRequestedAt = dto.RefundRequestedAt,
                    refundedAt = dto.RefundedAt, amount = order.Total, refundTransactionId = dto.RefundTransactionId, note = dto.RefundError },
                    statusCode: order.RefundState == ApplicationCore.Entities.Payments.RefundState.Refunded ? 201 : 202);
            })
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff view of an order, with its payment and refund status.</summary>
public class AdminGetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (int orderId, OrderPaymentService orders, CancellationToken ct) =>
            {
                var order = await orders.GetOrderAsync(orderId, ct);
                await GetOrderEndpoint.TryRefresh(orders, order, ct);
                return Results.Ok(OrderPaymentService.ToDto(order, admin: true));
            })
            .Produces<OrderDto>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Sets up a monthly invoice for a business customer.</summary>
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (CreateInvoiceScheduleRequest request, BackOfficeService office, CancellationToken ct) =>
            {
                var dto = await office.CreateScheduleAsync(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{dto.ScheduleId}", dto);
            })
            .Produces<InvoiceScheduleDto>(201)
            .WithTags("AdminEndpoints");
    }
}

public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (string scheduleId, BackOfficeService office, CancellationToken ct)
                => Results.Ok(await office.PauseAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}

public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (string scheduleId, BackOfficeService office, CancellationToken ct)
                => Results.Ok(await office.ResumeAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Onboards a seller as a new merchant (uses the Stax platform key).</summary>
public class CreateSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Auth.ADMIN, AuthenticationSchemes = Auth.SCHEME)] async (CreateSellerRequest request, BackOfficeService office, CancellationToken ct) =>
            {
                var dto = await office.OnboardSellerAsync(request, ct);
                return Results.Created($"api/admin/sellers/{dto.MerchantId}", dto);
            })
            .Produces<SellerDto>(201)
            .WithTags("AdminEndpoints");
    }
}
