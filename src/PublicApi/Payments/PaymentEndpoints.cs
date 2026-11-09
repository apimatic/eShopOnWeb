using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Shared authorization helpers: identity and role always come from the JWT.</summary>
internal static class Caller
{
    public const string ADMIN_ROLES = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public static string Email(ClaimsPrincipal user) => ShopperService.Normalize(user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name));
}

/// <summary>Saved payment methods (shopper).</summary>
public class PaymentMethodEndpoints : IEndpoint
{
    private const string TAG = "PaymentEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
            {
                var dto = await service.Save(Caller.Email(user), request, ct);
                return Results.Created($"api/payment-methods/{dto.Id}", dto);
            })
            .Produces<PaymentMethodDto>(201).WithTags(TAG);

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
                Results.Ok(await service.List(Caller.Email(user), ct)))
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>().WithTags(TAG);

        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, ClaimsPrincipal user, PaymentMethodService service, CancellationToken ct) =>
            {
                await service.Remove(Caller.Email(user), paymentMethodId, ct);
                return Results.NoContent();
            })
            .WithTags(TAG);

        app.MapGet("api/admin/shoppers/{email}/payment-methods",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, PaymentMethodService service, CancellationToken ct) =>
                Results.Ok(await service.ForSupport(ShopperService.Normalize(email), ct)))
            .Produces<System.Collections.Generic.List<StaffPaymentMethodDto>>().WithTags(TAG);
    }
}

/// <summary>Orders, checkout, pay and order status (shopper).</summary>
public class OrderEndpoints : IEndpoint
{
    private const string TAG = "OrderEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
            {
                var dto = await service.Place(Caller.Email(user), request, ct);
                return Results.Created($"api/orders/{dto.OrderId}", dto);
            })
            .Produces<OrderDto>(201).WithTags(TAG);

        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.GetForShopper(Caller.Email(user), orderId, ct)))
            .Produces<OrderDto>().WithTags(TAG);

        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodChoiceRequest request, ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.Checkout(Caller.Email(user), orderId, request.PaymentMethodId, ct)))
            .Produces<CheckoutDto>().WithTags(TAG);

        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodChoiceRequest request, ClaimsPrincipal user, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.Pay(Caller.Email(user), orderId, request.PaymentMethodId, ct)))
            .Produces<OrderDto>().WithTags(TAG);
    }
}

/// <summary>Refunds and the staff order view.</summary>
public class AdminOrderEndpoints : IEndpoint
{
    private const string TAG = "AdminEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service, CancellationToken ct) =>
            {
                var dto = await service.Refund(orderId, ct);
                return dto.RefundStatus == "pending" ? Results.Accepted($"api/admin/orders/{orderId}", dto) : Results.Ok(dto);
            })
            .Produces<RefundDto>().WithTags(TAG);

        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.GetForStaff(orderId, ct)))
            .Produces<StaffOrderDto>().WithTags(TAG);
    }
}

/// <summary>Monthly invoice schedules and seller onboarding (staff).</summary>
public class AdminBillingEndpoints : IEndpoint
{
    private const string TAG = "AdminEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/invoice-schedules",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateInvoiceScheduleRequest request, InvoiceScheduleService service, CancellationToken ct) =>
            {
                var dto = await service.Create(request, ct);
                return Results.Created($"api/admin/invoice-schedules/{dto.ScheduleId}", dto);
            })
            .Produces<InvoiceScheduleDto>(201).WithTags(TAG);

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, InvoiceScheduleService service, CancellationToken ct) =>
                Results.Ok(await service.SetActive(scheduleId, false, ct)))
            .Produces<InvoiceScheduleDto>().WithTags(TAG);

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string scheduleId, InvoiceScheduleService service, CancellationToken ct) =>
                Results.Ok(await service.SetActive(scheduleId, true, ct)))
            .Produces<InvoiceScheduleDto>().WithTags(TAG);

        app.MapPost("api/admin/sellers",
            [Authorize(Roles = Caller.ADMIN_ROLES, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (OnboardSellerRequest request, SellerService service, CancellationToken ct) =>
            {
                var dto = await service.Onboard(request, ct);
                return Results.Created($"api/admin/sellers/{dto.SellerId}", dto);
            })
            .Produces<SellerDto>(201).WithTags(TAG);
    }
}

/// <summary>Closing a shopper account.</summary>
public class AccountEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, AccountService service, CancellationToken ct) =>
            {
                await service.Close(Caller.Email(user), ct);
                return Results.NoContent();
            })
            .WithTags("AccountEndpoints");
    }
}
