using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Stax;

internal static class Caller
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    /// <summary>The caller's identity, taken from the JWT.</summary>
    public static string Email(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue(ClaimTypes.Email)
                   ?? user.FindFirstValue("name")
                   ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(name)
            ? throw new RequestRefusedException(401, "The token does not say who the caller is.")
            : name;
    }
}

// ---- Feature 1: saved payment methods ----------------------------------------------------------

/// <summary>Saves a card or a bank account for the shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (SavePaymentMethodRequest request, ClaimsPrincipal user,
                PaymentMethodService service, CancellationToken ct) =>
            {
                var created = await service.SaveAsync(Caller.Email(user), request, ct);
                return Results.Created($"api/payment-methods/{created.Id}", created);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Lists the shopper's saved payment methods.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(Caller.Email(user), ct)))
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Removes one of the shopper's saved payment methods.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (string paymentMethodId, ClaimsPrincipal user,
                PaymentMethodService service, CancellationToken ct) =>
            {
                await service.RemoveAsync(Caller.Email(user), paymentMethodId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }
}

// ---- Features 2-5: orders, checkout, pay, status -----------------------------------------------

/// <summary>Places an order for catalog items.</summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (CreateOrderRequest request, ClaimsPrincipal user,
                ShopOrderService service, CancellationToken ct) =>
            {
                var order = await service.CreateAsync(Caller.Email(user), request, ct);
                return Results.Created($"api/orders/{order.Id}", order);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows the exact amount (fee included) the shopper pays with the chosen payment method.</summary>
public class CheckoutOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/checkout",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (int orderId, PaymentMethodChoiceRequest request,
                ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.CheckoutAsync(Caller.Email(user), orderId, request.PaymentMethodId, ct)))
            .Produces<CheckoutDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Pays an order with a saved card or bank account.</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (int orderId, PayOrderRequest request,
                ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.PayAsync(Caller.Email(user), orderId, request, ct)))
            .Produces<PayDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows one of the shopper's orders, including whether it is paid.</summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (int orderId, ClaimsPrincipal user,
                OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.GetAsync(Caller.Email(user), orderId, ct)))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

// ---- Feature 6: support ------------------------------------------------------------------------

/// <summary>Every payment method a shopper ever saved, removed ones included.</summary>
public class ListShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (string email,
                PaymentMethodService service, CancellationToken ct) =>
                Results.Ok(await service.ListForSupportAsync(email, ct)))
            .Produces<System.Collections.Generic.List<SupportPaymentMethodDto>>()
            .WithTags("AdminEndpoints");
    }
}

// ---- Feature 7: close an account ---------------------------------------------------------------

/// <summary>Closes the caller's shop account and erases their data, here and at Stax.</summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = Caller.Scheme)] async (ClaimsPrincipal user, AccountService service, CancellationToken ct) =>
                Results.Ok(await service.CloseAsync(Caller.Email(user), ct)))
            .Produces<CloseAccountDto>()
            .WithTags("AccountEndpoints");
    }
}

// ---- Feature 8: refunds ------------------------------------------------------------------------

/// <summary>Refunds the whole amount of an order.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId:int}/refunds",
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (int orderId,
                OrderPaymentService service, CancellationToken ct, [FromBody] RefundOrderRequest? request = null) =>
            {
                var refund = await service.RefundAsync(orderId, request, ct);
                return refund.RefundStatus == "refunded" ? Results.Ok(refund) : Results.Accepted($"api/admin/orders/{orderId}", refund);
            })
            .Produces<RefundDto>()
            .Produces<RefundDto>(StatusCodes.Status202Accepted)
            .WithTags("AdminEndpoints");
    }
}

/// <summary>The staff view of an order.</summary>
public class GetOrderForStaffEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId:int}",
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (int orderId,
                OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.GetForStaffAsync(orderId, ct)))
            .Produces<StaffOrderDto>()
            .WithTags("AdminEndpoints");
    }
}

// ---- Feature 9: monthly invoices ---------------------------------------------------------------

public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (CreateInvoiceScheduleRequest request,
                InvoiceScheduleService service, CancellationToken ct) =>
            {
                var schedule = await service.CreateAsync(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{schedule.ScheduleId}", schedule);
            })
            .Produces<InvoiceScheduleDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }
}

public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (string scheduleId,
                InvoiceScheduleService service, CancellationToken ct) =>
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
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (string scheduleId,
                InvoiceScheduleService service, CancellationToken ct) =>
                Results.Ok(await service.ResumeAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");
    }
}

// ---- Feature 10: sellers -----------------------------------------------------------------------

public class CreateSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Caller.Admins, AuthenticationSchemes = Caller.Scheme)] async (CreateSellerRequest request,
                SellerService service, CancellationToken ct) =>
            {
                var seller = await service.OnboardAsync(request, ct);
                return Results.Created($"api/admin/sellers/{seller.SellerId}", seller);
            })
            .Produces<SellerDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }
}
