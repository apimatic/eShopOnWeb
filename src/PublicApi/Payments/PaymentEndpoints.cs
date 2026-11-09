using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The endpoints of the Stax payments integration. The caller (a shopper, or staff in the administrator
/// role) always comes from the JWT; a shopper only ever reaches their own data.
/// </summary>
public class PaymentMethodEndpoints : IEndpoint
{
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;
    private const string Tag = "PaymentEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = Jwt)] async (ClaimsPrincipal user, SavePaymentMethodRequest request,
                    ShopperResolver shoppers, PaymentMethodService methods, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    var saved = await methods.SaveAsync(shopper.UserName!, request, ct);
                    return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved);
                })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags(Tag);

        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = Jwt)] async (ClaimsPrincipal user,
                    ShopperResolver shoppers, PaymentMethodService methods, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    return Results.Ok(await methods.ListAsync(shopper.UserName!, ct));
                })
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags(Tag);

        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = Jwt)] async (string paymentMethodId, ClaimsPrincipal user,
                    ShopperResolver shoppers, PaymentMethodService methods, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    await methods.RemoveAsync(shopper.UserName!, paymentMethodId, ct);
                    return Results.NoContent();
                })
            .WithTags(Tag);

        app.MapGet("api/admin/shoppers/{email}/payment-methods",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (string email,
                    PaymentMethodService methods, CancellationToken ct) =>
                    Results.Ok(await methods.ListAllForSupportAsync(email, ct)))
            .Produces<System.Collections.Generic.List<PaymentMethodDto>>()
            .WithTags(Tag);
    }
}

public class OrderEndpoints : IEndpoint
{
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;
    private const string Tag = "PaymentEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = Jwt)] async (ClaimsPrincipal user, PlaceOrderRequest request,
                    ShopperResolver shoppers, PaymentProcessor payments, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    var order = await payments.PlaceOrderAsync(shopper.UserName!, request, ct);
                    return Results.Created($"api/orders/{order.OrderId}", order);
                })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags(Tag);

        app.MapGet("api/orders/{orderId:int}",
                [Authorize(AuthenticationSchemes = Jwt)] async (int orderId, ClaimsPrincipal user,
                    ShopperResolver shoppers, PaymentProcessor payments, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    return Results.Ok(await payments.GetOrderAsync(orderId, shopper.UserName!, ct));
                })
            .Produces<OrderDto>()
            .WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/checkout",
                [Authorize(AuthenticationSchemes = Jwt)] async (int orderId, PayWithMethodRequest request,
                    ClaimsPrincipal user, ShopperResolver shoppers, PaymentProcessor payments, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    return Results.Ok(await payments.CheckoutAsync(shopper.UserName!, orderId, request.PaymentMethodId, ct));
                })
            .Produces<CheckoutDto>()
            .WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = Jwt)] async (int orderId, PayWithMethodRequest request,
                    ClaimsPrincipal user, ShopperResolver shoppers, PaymentProcessor payments, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    return Results.Ok(await payments.PayAsync(shopper.UserName!, orderId, request.PaymentMethodId, ct));
                })
            .Produces<PayDto>()
            .WithTags(Tag);

        app.MapPost("api/account/close",
                [Authorize(AuthenticationSchemes = Jwt)] async (ClaimsPrincipal user,
                    ShopperResolver shoppers, AccountService accounts, CancellationToken ct) =>
                {
                    var shopper = await shoppers.RequireAsync(user);
                    await accounts.CloseAsync(shopper, ct);
                    return Results.NoContent();
                })
            .WithTags(Tag);
    }
}

public class AdminPaymentEndpoints : IEndpoint
{
    private const string Jwt = JwtBearerDefaults.AuthenticationScheme;
    private const string Tag = "PaymentEndpoints";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId:int}/refunds",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (int orderId,
                    PaymentProcessor payments, CancellationToken ct) =>
                {
                    var refund = await payments.RefundAsync(orderId, ct);
                    return refund.RefundStatus == "refunded"
                        ? Results.Ok(refund)
                        : Results.Json(refund, statusCode: StatusCodes.Status202Accepted);
                })
            .Produces<RefundDto>()
            .WithTags(Tag);

        app.MapGet("api/admin/orders/{orderId:int}",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (int orderId,
                    PaymentProcessor payments, CancellationToken ct) =>
                    Results.Ok(await payments.GetOrderAsync(orderId, null, ct)))
            .Produces<OrderDto>()
            .WithTags(Tag);

        app.MapPost("api/admin/invoice-schedules",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (
                    CreateInvoiceScheduleRequest request, BillingService billing, CancellationToken ct) =>
                {
                    var schedule = await billing.CreateScheduleAsync(request, ct);
                    return Results.Created($"api/admin/invoice-schedules/{schedule.ScheduleId}", schedule);
                })
            .Produces<InvoiceScheduleDto>(StatusCodes.Status201Created)
            .WithTags(Tag);

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/pause",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (string scheduleId,
                    BillingService billing, CancellationToken ct) =>
                    Results.Ok(await billing.SetActiveAsync(scheduleId, false, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags(Tag);

        app.MapPost("api/admin/invoice-schedules/{scheduleId}/resume",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (string scheduleId,
                    BillingService billing, CancellationToken ct) =>
                    Results.Ok(await billing.SetActiveAsync(scheduleId, true, ct)))
            .Produces<InvoiceScheduleDto>()
            .WithTags(Tag);

        app.MapPost("api/admin/sellers",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = Jwt)] async (
                    OnboardSellerRequest request, BillingService billing, CancellationToken ct) =>
                {
                    var seller = await billing.OnboardSellerAsync(request, ct);
                    return Results.Created($"api/admin/sellers/{seller.SellerId}", seller);
                })
            .Produces<SellerDto>(StatusCodes.Status201Created)
            .WithTags(Tag);
    }
}
