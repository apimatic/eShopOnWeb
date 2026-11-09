using System;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Closes the caller's account and erases their data, at Stax and in the shop.</summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, ShopperAccessor shopper, UserManager<ApplicationUser> users, CatalogContext db, StaxClient stax, StaxCustomers customers, CancellationToken ct) =>
            {
                var user = await shopper.UserAsync(principal);
                var email = user.Email ?? user.UserName!;

                // Stax first: if it fails nothing is lost locally and the request can be repeated.
                foreach (var customer in await customers.FindAsync(email, ct))
                {
                    var customerId = (string)customer["id"]!;
                    foreach (var m in await customers.ListMethodsAsync(customerId, false, ct))
                    {
                        var id = StaxClient.Q((string)m["id"]!);
                        await stax.PutAsync("payment-method/" + id, new
                        {
                            person_name = "Closed Account",
                            address_1 = "", address_2 = "", address_city = "", address_state = "", address_zip = "", address_country = ""
                        }, ct);
                        await stax.DeleteAsync("payment-method/" + id, ct);
                    }
                    foreach (var m in await customers.ListMethodsAsync(customerId, true, ct))
                    {
                        try
                        {
                            await stax.PutAsync("payment-method/" + StaxClient.Q((string)m["id"]!), new
                            {
                                person_name = "Closed Account",
                                address_1 = "", address_2 = "", address_city = "", address_state = "", address_zip = "", address_country = ""
                            }, ct);
                        }
                        catch (ApiException)
                        {
                            // Already-removed methods may be read-only at Stax; the live ones above are what matter.
                        }
                    }
                    await stax.PutAsync("customer/" + StaxClient.Q(customerId), new
                    {
                        firstname = "Closed",
                        lastname = "Account",
                        company = "",
                        email = "",
                        phone = "",
                        address_1 = "", address_2 = "", address_city = "", address_state = "", address_zip = "", address_country = "",
                        reference = "",
                        notes = "",
                        cc_emails = Array.Empty<string>(),
                        cc_sms = Array.Empty<string>()
                    }, ct);
                    await stax.DeleteAsync("customer/" + StaxClient.Q(customerId), ct);
                }

                var buyerIds = new[] { email, user.UserName! }.Distinct().ToList();
                var orders = await db.Orders.Include(o => o.OrderItems).Where(o => buyerIds.Contains(o.BuyerId)).ToListAsync(ct);
                var orderIds = orders.Select(o => o.Id).ToList();
                db.OrderPayments.RemoveRange(await db.OrderPayments.Where(p => orderIds.Contains(p.OrderId) || buyerIds.Contains(p.BuyerId)).ToListAsync(ct));
                db.Orders.RemoveRange(orders);
                db.Baskets.RemoveRange(await db.Baskets.Include(b => b.Items).Where(b => buyerIds.Contains(b.BuyerId)).ToListAsync(ct));
                await db.SaveChangesAsync(ct);

                var deleted = await users.DeleteAsync(user);
                if (!deleted.Succeeded)
                    throw new ApiException(500, "Could not delete the shop account: " + string.Join(" ", deleted.Errors.Select(e => e.Description)));
                return Results.Ok(new { closed = true });
            })
            .WithTags("AccountEndpoints");
    }
}

public record InvoiceScheduleRequest(string? Email, string? Company, decimal? Amount, string? FirstInvoiceDate);

/// <summary>Monthly invoices for a business customer.</summary>
public static class InvoiceSchedules
{
    // Stax requires the hosted page address on every schedule; invoices generated by it are paid there.
    public const string HostedInvoiceUrl = "https://app.staxpayments.com/#/bill/";

    public static object View(JsonNode s) => new
    {
        id = (string?)s["id"],
        customerId = (string?)s["customer_id"],
        active = (bool?)s["active"] ?? false,
        rule = (string?)s["rule"],
        nextRunAt = (string?)s["next_run_at"],
        lastRunAt = (string?)s["last_run_at"]
    };

    public static DateTime ParseDtStart(string? rule)
    {
        var m = Regex.Match(rule ?? "", @"DTSTART=(\d{8})T(\d{6})Z");
        if (!m.Success) throw new ApiException(422, "The schedule's recurrence rule has no start date.");
        return DateTime.ParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
    }

    /// <summary>The first monthly occurrence after <paramref name="after"/>, keeping the start's day of month.</summary>
    public static DateTime NextOccurrence(DateTime start, DateTime after)
    {
        var months = (after.Year - start.Year) * 12 + after.Month - start.Month;
        if (months < 0) months = 0;
        while (true)
        {
            var c = start.AddMonths(months);
            if (c > after) return c;
            months++;
        }
    }
}

public class CreateInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (InvoiceScheduleRequest request, StaxClient stax, StaxCustomers customers, CancellationToken ct) =>
            {
                var email = request.Email?.Trim();
                if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out _)) throw new ApiException(400, "A valid email is required.");
                if (string.IsNullOrWhiteSpace(request.Company)) throw new ApiException(400, "company is required.");
                if (request.Amount is null or <= 0) throw new ApiException(400, "amount must be greater than zero.");
                var amount = Math.Round(request.Amount.Value, 2);

                var now = DateTime.UtcNow;
                DateTime start;
                if (string.IsNullOrWhiteSpace(request.FirstInvoiceDate))
                {
                    start = now.AddMinutes(1);
                }
                else
                {
                    if (!DateTime.TryParseExact(request.FirstInvoiceDate.Trim(), "yyyy-MM-dd", null,
                            System.Globalization.DateTimeStyles.None, out var day))
                        throw new ApiException(400, "firstInvoiceDate must be a date like 2026-12-31.");
                    if (day.Date < now.Date) throw new ApiException(400, "firstInvoiceDate cannot be in the past.");
                    start = day.Date == now.Date ? now.AddMinutes(1) : day.Date.AddHours(12);
                }

                var customerId = await customers.FindOrCreateAsync(email, request.Company!.Trim(), ct);
                var schedule = await stax.PostAsync("invoice/schedule", new
                {
                    url = InvoiceSchedules.HostedInvoiceUrl,
                    total = amount,
                    customer_id = customerId,
                    rule = $"DTSTART={start:yyyyMMdd'T'HHmmss'Z'};FREQ=MONTHLY",
                    meta = new
                    {
                        subtotal = amount,
                        lineItems = new[] { new { item = "Monthly invoice", quantity = 1, price = amount } }
                    }
                }, ct);
                return Results.Created($"api/admin/invoice-schedules/{(string?)schedule["id"]}", InvoiceSchedules.View(schedule));
            })
            .WithTags("AdminInvoiceEndpoints");
    }
}

public class PauseInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, StaxClient stax, CancellationToken ct) =>
            {
                var schedule = await stax.PutAsync("invoice/schedule/" + StaxClient.Q(scheduleId), new { active = false }, ct);
                return Results.Ok(InvoiceSchedules.View(schedule));
            })
            .WithTags("AdminInvoiceEndpoints");
    }
}

public class ResumeInvoiceScheduleEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, StaxClient stax, CancellationToken ct) =>
            {
                var id = StaxClient.Q(scheduleId);
                var current = await stax.GetAsync("invoice/schedule/" + id, ct);
                if ((bool?)current["active"] == true) return Results.Ok(InvoiceSchedules.View(current));

                // Stax needs a future run date to resume; continue on the schedule's own monthly rhythm.
                var next = InvoiceSchedules.NextOccurrence(InvoiceSchedules.ParseDtStart((string?)current["rule"]), DateTime.UtcNow.AddMinutes(1));
                var schedule = await stax.PutAsync("invoice/schedule/" + id,
                    new { active = true, next_run_at = next.ToString("yyyy-MM-dd HH:mm:ss") }, ct);
                return Results.Ok(InvoiceSchedules.View(schedule));
            })
            .WithTags("AdminInvoiceEndpoints");
    }
}

public record SellerContact(string? Name, string? Email, string? Phone);
public record OnboardSellerRequest(string? Company, SellerContact? Contact);

/// <summary>Onboards a seller as a new merchant, through the Stax partner (platform) API.</summary>
public class OnboardSellerEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/sellers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (OnboardSellerRequest request, StaxClient stax, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Company)) throw new ApiException(400, "company is required.");
                var contact = request.Contact;
                if (contact == null || string.IsNullOrWhiteSpace(contact.Email) || !MailAddress.TryCreate(contact.Email.Trim(), out _))
                    throw new ApiException(400, "contact.email must be a valid email.");
                if (string.IsNullOrWhiteSpace(contact.Name)) throw new ApiException(400, "contact.name is required.");
                if (!stax.HasPlatformKey)
                    throw new ApiException(422, "Sellers cannot be onboarded: the shop has no Stax platform key (Stax:PlatformApiKey).");

                var names = contact.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var email = contact.Email.Trim();
                var password = Guid.NewGuid().ToString("N") + "aA1!";
                var result = await stax.PlatformPostAsync("admin/enroll", new
                {
                    merchant = new { plan = "premium", company_name = request.Company.Trim(), contact_email = email },
                    user = new { email, name = contact.Name.Trim(), password, password_confirmation = password },
                    registration = new
                    {
                        business_legal_name = request.Company.Trim(),
                        business_email = email,
                        email,
                        first_name = names[0],
                        last_name = names.Length > 1 ? names[1] : names[0],
                        business_phone_number = contact.Phone
                    }
                }, ct);
                return Results.Created($"api/admin/sellers/{(string?)result["merchant"]?["id"]}", new
                {
                    merchantId = (string?)result["merchant"]?["id"],
                    company = (string?)result["merchant"]?["company_name"] ?? request.Company.Trim(),
                    status = (string?)result["merchant"]?["status"],
                    contactEmail = email
                });
            })
            .WithTags("AdminSellerEndpoints");
    }
}
