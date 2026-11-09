using System.Security.Claims;
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
/// Places an order for catalog items
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, ShopperDirectory shoppers, OrderPaymentService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                var order = await service.CreateOrder(email, request, ct);
                return Results.Created($"api/orders/{order.OrderId}", order);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Shows an order of the shopper in the JWT, with whether its payment has gone through
/// </summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, ShopperDirectory shoppers, OrderPaymentService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                return Results.Ok(await service.GetOrder(email, orderId, ct));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Shows the exact amount (fee included) the shopper will pay with a payment method
/// </summary>
public class CheckoutOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodChoiceRequest request, ClaimsPrincipal user, ShopperDirectory shoppers, OrderPaymentService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                return Results.Ok(await service.Checkout(email, orderId, request.PaymentMethodId, ct));
            })
            .Produces<CheckoutDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Pays an order with a saved card or bank account
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentMethodChoiceRequest request, ClaimsPrincipal user, ShopperDirectory shoppers, OrderPaymentService service, CancellationToken ct) =>
            {
                var email = await shoppers.RequireShopper(user);
                return Results.Ok(await service.Pay(email, orderId, request.PaymentMethodId, ct));
            })
            .Produces<PayDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Staff: refunds the whole amount of an order
/// </summary>
public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/admin/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service, CancellationToken ct) =>
            {
                var refund = await service.Refund(orderId, ct);
                return refund.RefundStatus == "refunded" ? Results.Ok(refund) : Results.Json(refund, statusCode: StatusCodes.Status202Accepted);
            })
            .Produces<RefundDto>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Staff: shows an order with its payment and refund state
/// </summary>
public class GetOrderForStaffEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/admin/orders/{orderId}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service, CancellationToken ct) =>
                Results.Ok(await service.GetOrderForStaff(orderId, ct)))
            .Produces<AdminOrderDto>()
            .WithTags("OrderEndpoints");
    }
}
