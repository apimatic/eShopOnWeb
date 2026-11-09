using System;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Stax;

internal static class Auth
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admin = Constants.Roles.ADMINISTRATORS;
}

// ---------------------------------------------------------------- 1. saved payment methods
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (SavePaymentMethodRequest request, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, PaymentMethodService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                var created = await service.SaveAsync(Shopper.EmailOf(user), request, ct);
                return Results.Created($"api/payment-methods/{created.Id}", created);
            })
            .Produces<PaymentMethodDto>(201)
            .WithTags("PaymentMethodEndpoints");
    }
}

public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, PaymentMethodService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                return Results.Ok(await service.ListAsync(Shopper.EmailOf(user), ct));
            })
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}

public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (string paymentMethodId, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, PaymentMethodService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                await service.RemoveAsync(Shopper.EmailOf(user), paymentMethodId, ct);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}

// ---------------------------------------------------------------- 2-5. orders
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (CreateOrderRequest request, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, ShopOrderService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                var order = await service.CreateAsync(Shopper.EmailOf(user), request, ct);
                return Results.Created($"api/orders/{order.Id}", order);
            })
            .Produces<OrderDto>(201)
            .WithTags("OrderEndpoints");
    }
}

public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/checkout",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (int orderId, PaymentMethodRequest request, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, ShopOrderService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                return Results.Ok(await service.CheckoutAsync(Shopper.EmailOf(user), orderId, request, ct));
            })
            .Produces<CheckoutDto>()
            .WithTags("OrderEndpoints");
    }
}

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (int orderId, PaymentMethodRequest request, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, ShopOrderService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                return Results.Ok(await service.PayAsync(Shopper.EmailOf(user), orderId, request, ct));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (int orderId, ClaimsPrincipal principal,
                UserManager<ApplicationUser> users, ShopOrderService service, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                return Results.Ok(await service.GetAsync(Shopper.EmailOf(user), orderId, ct));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

// ---------------------------------------------------------------- 6. support view
public class ShopperPaymentMethodsForSupportEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (string email, PaymentMethodService service, CancellationToken ct) =>
            {
                if (!InvoiceService.IsEmail(email)) throw ApiException.BadRequest("A valid shopper email is required.");
                return Results.Ok(await service.ListForSupportAsync(email, ct));
            })
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags("AdminEndpoints");
    }
}

// ---------------------------------------------------------------- 7. close account
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = Auth.Scheme)] async (ClaimsPrincipal principal, UserManager<ApplicationUser> users,
                PaymentMethodService methods, ShopOrderService orders, CatalogContext catalog, CancellationToken ct) =>
            {
                var user = await Shopper.RequireAsync(principal, users);
                var email = Shopper.EmailOf(user);

                if (await orders.HasPendingMoneyAsync(email, ct))
                    throw ApiException.Conflict("The account cannot be closed while a payment or refund is still in progress.");

                // Stax first: if it fails nothing local is erased and the call can simply be repeated.
                await methods.EraseAtStaxAsync(email, ct);

                var buyerIds = new[] { user.UserName, user.Email, user.Id }.Where(s => !string.IsNullOrEmpty(s)).ToList();
                catalog.Baskets.RemoveRange(await catalog.Baskets.Include(b => b.Items).Where(b => buyerIds.Contains(b.BuyerId)).ToListAsync(ct));
                catalog.Orders.RemoveRange(await catalog.Orders.Include(o => o.OrderItems).Where(o => buyerIds.Contains(o.BuyerId)).ToListAsync(ct));
                await catalog.SaveChangesAsync(ct);
                await orders.AnonymizeAsync(email, ct);
                await methods.ForgetLocalAsync(email, ct);

                var deleted = await users.DeleteAsync(user);
                if (!deleted.Succeeded)
                    throw new ApiException(500, "The account data was erased but the login could not be removed: " + string.Join("; ", deleted.Errors.Select(e => e.Description)));
                return Results.Ok(new { closed = true });
            })
            .WithTags("AccountEndpoints");
    }
}

// ---------------------------------------------------------------- 8. refunds
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId:int}/refunds",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (int orderId, ShopOrderService service, CancellationToken ct) =>
            {
                var order = await service.RefundAsync(orderId, ct);
                var body = new { orderId = order.Id, refundStatus = order.RefundStatus, amount = order.Total, refundedAmount = order.RefundedAmount, note = order.RefundNote, order };
                return order.RefundStatus == "refunded" ? Results.Ok(body) : Results.Accepted($"api/admin/orders/{order.Id}", body);
            })
            .WithTags("AdminEndpoints");
    }
}

public class GetOrderForAdminEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId:int}",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (int orderId, ShopOrderService service, CancellationToken ct) =>
                Results.Ok(await service.GetForAdminAsync(orderId, ct)))
            .Produces<AdminOrderDto>()
            .WithTags("AdminEndpoints");
    }
}

// ---------------------------------------------------------------- 9. invoice schedules
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (CreateInvoiceScheduleRequest request, InvoiceService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{created.ScheduleId}", created);
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
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (string scheduleId, InvoiceService service, CancellationToken ct) =>
                Results.Ok(await service.PauseAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}

public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (string scheduleId, InvoiceService service, CancellationToken ct) =>
                Results.Ok(await service.ResumeAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}

// ---------------------------------------------------------------- 10. sellers
public record OnboardSellerRequest(string? Company, string? ContactName, string? Contact, string? ContactEmail, string? Email, string? ContactPhone, string? Phone);

public class OnboardSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Auth.Admin, AuthenticationSchemes = Auth.Scheme)] async (OnboardSellerRequest request, StaxClient stax, CancellationToken ct) =>
            {
                var email = (request.ContactEmail ?? request.Email)?.Trim();
                var name = (request.ContactName ?? request.Contact)?.Trim();
                if (string.IsNullOrWhiteSpace(request.Company)) throw ApiException.BadRequest("company is required.");
                if (!InvoiceService.IsEmail(email)) throw ApiException.BadRequest("A valid contact email is required.");

                var merchant = await stax.PostAsPlatformAsync("/merchant", new
                {
                    company_name = request.Company.Trim(),
                    contact_name = name,
                    contact_email = email,
                    contact_phone = request.ContactPhone ?? request.Phone,
                }, ct);
                var dto = new
                {
                    sellerId = StaxClient.Str(merchant, "id"),
                    company = StaxClient.Str(merchant, "company_name"),
                    contactEmail = StaxClient.Str(merchant, "contact_email"),
                    status = StaxClient.Str(merchant, "status"),
                };
                return Results.Created($"api/admin/sellers/{dto.sellerId}", dto);
            })
            .WithTags("AdminEndpoints");
    }
}
