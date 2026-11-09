using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Staff features: support view, refunds and invoice schedules.</summary>
public class AdminEndpoints : IEndpoint
{
    private const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (string email,
                PaymentMethodService methods, CancellationToken ct) =>
            {
                var list = await methods.ListForSupportAsync(email, ct);
                return list == null
                    ? throw ApiException.NotFound("No shopper with that email is known to Stax.")
                    : Results.Ok(list);
            })
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/orders/{orderId:int}/refunds",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (int orderId,
                OrderPaymentService orders, CancellationToken ct) =>
            {
                var order = await orders.RefundAsync(orderId, ct);
                return Results.Ok(new
                {
                    orderId = order.Id,
                    refundStatus = order.RefundStatus,
                    amount = order.Total,
                    refundTransactionId = order.RefundTransactionId,
                    message = order.RefundMessage,
                });
            })
            .WithTags("AdminEndpoints");

        app.MapGet("api/admin/orders/{orderId:int}",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (int orderId,
                OrderPaymentService orders, CancellationToken ct) =>
            {
                var order = await orders.GetOrderAsync(orderId, ct);
                await orders.SyncPaymentAsync(order, ct);
                await orders.TryRefundAsync(order, ct);
                return Results.Ok(OrderPaymentService.ToStaffDto(order));
            })
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (JsonElement body,
                InvoiceScheduleService schedules, CancellationToken ct) =>
            {
                var b = LooseBody.From(body);
                var email = b.Str("email", "customerEmail")
                    ?? throw ApiException.BadRequest("'email' is required.");
                var company = b.Str("company", "companyName")
                    ?? throw ApiException.BadRequest("'company' is required.");
                var amount = b.Dec("amount", "total")
                    ?? throw ApiException.BadRequest("'amount' is required.");
                DateTime? start = null;
                var startText = b.Str("startDate", "firstInvoiceDate", "firstInvoiceOn", "start");
                if (startText != null)
                {
                    if (!DateTime.TryParse(startText, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
                        throw ApiException.BadRequest("'startDate' must be a date like 2026-12-01.");
                    start = parsed;
                }
                var record = await schedules.CreateAsync(email.Trim().ToLowerInvariant(), company, amount, start, ct);
                return Results.Created($"api/admin/invoice-schedules/{record.Id}", InvoiceScheduleService.ToDto(record));
            })
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (string scheduleId,
                InvoiceScheduleService schedules, CancellationToken ct) =>
                Results.Ok(InvoiceScheduleService.ToDto(await schedules.PauseAsync(scheduleId, ct))))
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (string scheduleId,
                InvoiceScheduleService schedules, CancellationToken ct) =>
                Results.Ok(InvoiceScheduleService.ToDto(await schedules.ResumeAsync(scheduleId, ct))))
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (JsonElement body,
                SellerService sellers, CancellationToken ct) =>
            {
                var seller = await sellers.OnboardAsync(LooseBody.From(body), ct);
                return Results.Created($"api/admin/sellers/{seller.Id}", new
                {
                    sellerId = seller.Id,
                    merchantId = seller.StaxMerchantId,
                    company = seller.Company,
                    contactName = seller.ContactName,
                    contactEmail = seller.ContactEmail,
                });
            })
            .WithTags("AdminEndpoints");
    }
}
