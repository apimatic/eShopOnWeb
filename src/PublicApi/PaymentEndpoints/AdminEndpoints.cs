using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Stax;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record CreateInvoiceScheduleRequest(string? CustomerEmail, string? Company, decimal? Amount, DateOnly? FirstInvoiceDate);
public record CreateSellerRequest(string? Company, string? ContactName, string? ContactEmail, string? ContactPhone);

internal static class AdminEndpoint
{
    public const string Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
}

/// <summary>Staff: every payment method a shopper has ever saved, removed ones included</summary>
public class ShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (string email, ShopperAccess access, PaymentMethodService methods, CancellationToken ct) =>
            {
                var buyerId = await access.FindBuyerIdByEmailAsync(email);
                if (buyerId is null) return Results.NotFound(new { message = "No such shopper." });
                return Results.Ok(await methods.SupportViewAsync(buyerId, ct));
            })
            .Produces<IReadOnlyList<SupportPaymentMethodView>>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff: an order with its payment and refund state</summary>
public class AdminGetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (int orderId, OrderPaymentService orders, CancellationToken ct) =>
                Results.Ok(await orders.GetAdminOrderAsync(orderId, ct)))
            .Produces<AdminOrderView>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff: refunds an order, in full</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (int orderId, ClaimsPrincipal user, OrderPaymentService orders, CancellationToken ct) =>
            {
                var result = await orders.RefundAsync(user.Identity?.Name ?? "staff", orderId, ct);
                return Results.Json(result, statusCode: result.RefundStatus == "refunded" ? StatusCodes.Status201Created : StatusCodes.Status202Accepted);
            })
            .Produces<RefundResult>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff: sets up a monthly invoice for a business customer</summary>
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (CreateInvoiceScheduleRequest request, BillingService billing, CancellationToken ct) =>
            {
                var schedule = await billing.CreateScheduleAsync(request.CustomerEmail, request.Company, request.Amount, request.FirstInvoiceDate, ct);
                return Results.Created($"api/admin/invoice-schedules/{schedule.ScheduleId}", schedule);
            })
            .Produces<InvoiceScheduleView>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff: pauses a monthly invoice schedule</summary>
public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (string scheduleId, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleView>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff: resumes a paused monthly invoice schedule</summary>
public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (string scheduleId, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleView>()
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Administrator: onboards a seller as a new merchant</summary>
public class OnboardSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = AdminEndpoint.Roles, AuthenticationSchemes = AdminEndpoint.Scheme)] async
            (CreateSellerRequest request, BillingService billing, CancellationToken ct) =>
            {
                var seller = await billing.OnboardSellerAsync(request.Company, request.ContactName, request.ContactEmail, request.ContactPhone, ct);
                return Results.Created($"api/admin/sellers/{seller.SellerId}", seller);
            })
            .Produces<SellerView>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }
}
