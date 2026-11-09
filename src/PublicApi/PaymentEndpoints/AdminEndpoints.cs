using System;
using System.Collections.Generic;
using System.Globalization;
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

internal static class AdminAuth
{
    public const string ROLES = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    public const string SCHEME = JwtBearerDefaults.AuthenticationScheme;
}

/// <summary>Support: every card and bank account a shopper has saved, removed ones included.</summary>
public class ShopperPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (string email,
                PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var valid = PaymentsService.ValidEmail(Uri.UnescapeDataString(email));
                return Results.Ok(await payments.SupportPaymentMethods(valid, ct));
            }))
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff refund an order in full; the shop keeps trying until the shopper has the money back.</summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (string orderId,
                ClaimsPrincipal user, PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var order = await payments.RequestRefund(ApiProblem.ParseId(orderId, "order"), user.Identity?.Name ?? "staff", ct);
                var status = order.RefundStatus == RefundState.REFUNDED
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status202Accepted;
                return Results.Json(new
                {
                    orderId = order.Id,
                    refundStatus = order.RefundStatus,
                    amount = order.RefundAmount,
                    requestedAt = PaymentsService.Iso(order.RefundRequestedAt),
                    refundedAt = PaymentsService.Iso(order.RefundedAt),
                    note = order.RefundNote
                }, statusCode: status);
            }))
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Staff view of an order.</summary>
public class AdminGetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (string orderId,
                PaymentsService payments, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var order = await payments.AnyOrder(ApiProblem.ParseId(orderId, "order"), ct);
                try
                {
                    await payments.SyncPayment(order, ct);
                    await payments.RetryRefund(order, ct);
                }
                catch (Exception ex) when (ApiProblem.IsStaxFailure(ex))
                {
                    // Stax is unreachable right now; the background sync will catch up.
                }
                return Results.Ok(PaymentsService.OrderDto(order, staff: true));
            }))
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Sets up a monthly invoice for a business customer.</summary>
public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    // Where Stax sends customers to pay the invoices it generates.
    private const string INVOICE_URL = "https://app.staxpayments.com/#/bill/";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (JsonElement request,
                StaxService stax, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                if (request.ValueKind != JsonValueKind.Object) throw ApiProblem.BadRequest("A JSON object is required.");
                var email = PaymentsService.ValidEmail(First(request, "email", "customerEmail", "customer_email"));
                var company = First(request, "company", "companyName", "company_name", "customerCompany");
                if (string.IsNullOrWhiteSpace(company)) throw ApiProblem.BadRequest("company is required.");

                var amountText = First(request, "amount", "total", "monthlyAmount");
                if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                    throw ApiProblem.BadRequest("amount must be a positive number.");
                amount = Math.Round(amount, 2);

                var now = DateTime.UtcNow;
                var today = now.Date;
                var start = today;
                var dateText = First(request, "firstInvoiceDate", "firstInvoiceOn", "startDate", "start_date", "startsOn", "firstInvoice", "date");
                if (!string.IsNullOrWhiteSpace(dateText))
                {
                    if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                        throw ApiProblem.BadRequest("The first invoice day must be a date such as 2026-12-01.");
                    start = parsed.Date;
                }
                if (start < today) throw ApiProblem.BadRequest("The first invoice day cannot be in the past.");

                // Stax only accepts a schedule that starts in the future, so "today" means a couple of minutes from now.
                var startAt = start == today
                    ? new[] { now.AddMinutes(2), today.AddDays(1).AddSeconds(-1) }.Min()
                    : start;
                var rule = "FREQ=MONTHLY";
                if (start.Day > 28)
                    rule += ";BYMONTHDAY=" + string.Join(",", Enumerable.Range(28, start.Day - 27)) + ";BYSETPOS=-1";
                rule += ";DTSTART=" + startAt.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

                // The customer: the one with this email and company, or a new one.
                var customers = await stax.FindCustomers(email, ct);
                var customer = customers.FirstOrDefault(c =>
                    string.Equals(c.Str("company"), company, StringComparison.OrdinalIgnoreCase));
                if (customer.ValueKind == JsonValueKind.Undefined)
                    customer = await stax.CreateCustomer(email, null, null, company, ct);
                var customerId = customer.GuidOf("id") ?? throw new ApiProblem(424, "Stax did not return the customer's id.");

                var schedule = await stax.CreateInvoiceSchedule(new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId,
                    ["total"] = amount,
                    ["url"] = INVOICE_URL,
                    ["rule"] = rule,
                    ["meta"] = new Dictionary<string, object?>
                    {
                        ["lineItems"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["item"] = "Monthly invoice",
                                ["details"] = company,
                                ["quantity"] = 1,
                                ["price"] = amount
                            }
                        }
                    }
                }, ct);

                return Results.Json(ScheduleDto(schedule, customerId, email, company, amount, start),
                    statusCode: StatusCodes.Status201Created);
            }))
            .WithTags("AdminEndpoints");
    }

    internal static object ScheduleDto(JsonElement schedule, Guid? customerId, string? email, string? company,
        decimal? amount, DateTime? firstInvoiceDate)
    {
        var active = schedule.Bool("active") ?? true;
        return new
        {
            scheduleId = schedule.GuidOf("id"),
            id = schedule.GuidOf("id"),
            customerId = customerId ?? schedule.GuidOf("customer_id"),
            email,
            company,
            amount = amount ?? schedule.Dec("total"),
            firstInvoiceDate = firstInvoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            active,
            status = active ? "active" : "paused",
            nextRunAt = PaymentsService.Iso(schedule.Time("next_run_at"))
        };
    }

    private static string? First(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.Str(n) is { Length: > 0 } s) return s.Trim();
        return null;
    }
}

/// <summary>Pauses a monthly invoice schedule.</summary>
public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (string scheduleId,
                StaxService stax, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var schedule = await stax.SetInvoiceScheduleActive(ApiProblem.ParseId(scheduleId, "invoice schedule"), false, ct);
                return Results.Ok(CreateInvoiceScheduleEndpoint.ScheduleDto(schedule, null, null, null, null, null));
            }))
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Resumes a paused monthly invoice schedule.</summary>
public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (string scheduleId,
                StaxService stax, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                var schedule = await stax.SetInvoiceScheduleActive(ApiProblem.ParseId(scheduleId, "invoice schedule"), true, ct);
                return Results.Ok(CreateInvoiceScheduleEndpoint.ScheduleDto(schedule, null, null, null, null, null));
            }))
            .WithTags("AdminEndpoints");
    }
}

/// <summary>Onboards a seller as a new Stax merchant under the shop's platform account.</summary>
public class OnboardSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = AdminAuth.ROLES, AuthenticationSchemes = AdminAuth.SCHEME)] (JsonElement request,
                StaxService stax, CancellationToken ct) => ApiGuard.Run(async () =>
            {
                if (request.ValueKind != JsonValueKind.Object) throw ApiProblem.BadRequest("A JSON object is required.");
                var company = First(request, "company", "companyName", "company_name", "name");
                if (string.IsNullOrWhiteSpace(company)) throw ApiProblem.BadRequest("company is required.");

                string? contactName, contactEmail, contactPhone;
                if (request.Get("contact") is { } contact && contact.ValueKind == JsonValueKind.Object)
                {
                    contactName = First(contact, "name", "fullName", "contactName");
                    contactEmail = First(contact, "email", "contactEmail");
                    contactPhone = First(contact, "phone", "contactPhone");
                }
                else
                {
                    var plain = request.Str("contact");
                    contactName = First(request, "contactName", "contact_name");
                    contactEmail = First(request, "contactEmail", "contact_email", "email") ??
                                   (plain is not null && plain.Contains('@') ? plain : null);
                    contactName ??= plain is not null && !plain.Contains('@') ? plain : null;
                    contactPhone = First(request, "contactPhone", "contact_phone", "phone");
                }
                contactEmail = PaymentsService.ValidEmail(contactEmail);

                // Created with the platform key only: without one the shop cannot onboard sellers.
                if (!stax.PlatformConfigured)
                    throw new ApiProblem(422, "Seller onboarding is unavailable: Stax:PlatformApiKey is not configured.");

                var body = new Dictionary<string, object?>
                {
                    ["company_name"] = company,
                    ["contact_email"] = contactEmail
                };
                if (contactName is not null) body["contact_name"] = contactName;
                if (contactPhone is not null) body["contact_phone"] = contactPhone;

                var merchant = await stax.CreateMerchant(body, ct);
                return Results.Json(new
                {
                    sellerId = merchant.GuidOf("id"),
                    merchantId = merchant.GuidOf("id"),
                    company = merchant.Str("company_name") ?? company,
                    contactEmail = merchant.Str("contact_email") ?? contactEmail,
                    status = merchant.Str("status")
                }, statusCode: StatusCodes.Status201Created);
            }))
            .WithTags("AdminEndpoints");
    }

    private static string? First(JsonElement e, params string[] names)
    {
        foreach (var n in names)
            if (e.Str(n) is { Length: > 0 } s) return s.Trim();
        return null;
    }
}
