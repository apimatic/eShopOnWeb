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
/// Staff endpoints (administrator role from the JWT).
/// </summary>
public class AdminPaymentEndpoints : IEndpoint
{
    private const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (string email, PaymentService payments, CancellationToken ct) =>
                Results.Ok(await payments.ListPaymentMethodsForSupportAsync(email, ct)))
            .Produces<ShopperPaymentMethodsResponse>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/orders/{orderId:int}/refunds",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (int orderId, PaymentService payments, CancellationToken ct) =>
            {
                var refund = await payments.RefundOrderAsync(orderId, ct);
                return refund.RefundStatus == "refunded" ? Results.Ok(refund) : Results.Accepted($"api/admin/orders/{orderId}", refund);
            })
            .Produces<RefundResponse>()
            .WithTags("AdminEndpoints");

        app.MapGet("api/admin/orders/{orderId:int}",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (int orderId, PaymentService payments, CancellationToken ct) =>
                Results.Ok(await payments.GetOrderForStaffAsync(orderId, ct)))
            .Produces<AdminOrderDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (CreateInvoiceScheduleRequest request, BillingService billing, CancellationToken ct) =>
            {
                var schedule = await billing.CreateScheduleAsync(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{schedule.ScheduleId}", schedule);
            })
            .Produces<InvoiceScheduleDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (string scheduleId, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.PauseScheduleAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (string scheduleId, BillingService billing, CancellationToken ct) =>
                Results.Ok(await billing.ResumeScheduleAsync(scheduleId, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Admins, AuthenticationSchemes = Scheme)] async (OnboardSellerRequest request, BillingService billing, CancellationToken ct) =>
            {
                var seller = await billing.OnboardSellerAsync(request, ct);
                return Results.Created($"api/admin/sellers/{seller.SellerId}", seller);
            })
            .Produces<SellerDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }
}
