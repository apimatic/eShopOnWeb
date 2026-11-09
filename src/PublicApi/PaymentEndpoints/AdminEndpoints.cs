using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Staff features, for the administrator role: support view, refunds, monthly invoices, sellers.</summary>
public class AdminEndpoints : IEndpoint
{
    private const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (string email, ShopperService shoppers) =>
                Results.Ok(await shoppers.StaffListMethodsAsync(email)))
            .Produces<StaffPaymentMethodDto[]>()
            .WithTags("AdminEndpoints");

        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (string orderId, OrderService orders) =>
                Results.Ok(await orders.StaffGetAsync(OrderId(orderId))))
            .Produces<StaffOrderDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (string orderId, OrderService orders) =>
                Results.Ok(await orders.RefundAsync(OrderId(orderId))))
            .Produces<RefundDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (CreateInvoiceScheduleRequest request, InvoiceAndSellerService svc) =>
            {
                var created = await svc.CreateScheduleAsync(request);
                return Results.Created($"api/admin/invoice-schedules/{created.Id}", created);
            })
            .Produces<InvoiceScheduleDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (string scheduleId, InvoiceAndSellerService svc) =>
                Results.Ok(await svc.PauseAsync(scheduleId)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (string scheduleId, InvoiceAndSellerService svc) =>
                Results.Ok(await svc.ResumeAsync(scheduleId)))
            .Produces<InvoiceScheduleDto>()
            .WithTags("AdminEndpoints");

        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Admins, AuthenticationSchemes = Jwt)] async (OnboardSellerRequest request, InvoiceAndSellerService svc) =>
            {
                var seller = await svc.OnboardSellerAsync(request);
                return Results.Created($"api/admin/sellers/{seller.Id}", seller);
            })
            .Produces<SellerDto>(StatusCodes.Status201Created)
            .WithTags("AdminEndpoints");
    }

    private static Guid OrderId(string id) =>
        Guid.TryParse(id, out var g) ? g : throw PaymentException.NotFound("No such order.");
}
