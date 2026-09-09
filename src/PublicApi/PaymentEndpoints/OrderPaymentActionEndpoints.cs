using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total for the shopper, by card or a
/// saved card. Does not take the money. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderEndpoint.Args, IOrderPaymentService>
{
    public record Args(int OrderId, string? BuyerId, PayOrderRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService service) =>
                await HandleAsync(new Args(orderId, http.User.GetBuyerId(), request ?? new PayOrderRequest()), service))
            .Produces<PaymentStateDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(Args args, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(args.BuyerId))
            return Results.Unauthorized();

        var card = args.Body.Card?.ToCardDetails();
        var payment = await service.AuthorizeAsync(args.OrderId, args.BuyerId, card, args.Body.SavedCardId);
        return Results.Ok(PaymentStateDto.From(payment));
    }
}

/// <summary>POST /api/orders/{orderId}/fulfil — operator marks fulfilled; the money is captured here.</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(orderId, service))
            .Produces<PaymentStateDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service)
    {
        var payment = await service.FulfilAsync(orderId);
        return Results.Ok(PaymentStateDto.From(payment));
    }
}

/// <summary>POST /api/orders/{orderId}/cancel — operator cancels before fulfilment; held funds released.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(orderId, service))
            .Produces<PaymentStateDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service)
    {
        var payment = await service.CancelAsync(orderId);
        return Results.Ok(PaymentStateDto.From(payment));
    }
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — returns the shopper's captured order, in full or in part.
/// Carries a caller-supplied idempotency key; a repeat under the same key does not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderEndpoint.Args, IOrderPaymentService>
{
    public record Args(int OrderId, string? BuyerId, RefundOrderRequest Body);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IOrderPaymentService service) =>
                await HandleAsync(new Args(orderId, http.User.GetBuyerId(), request ?? new RefundOrderRequest()), service))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(Args args, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(args.BuyerId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(args.Body.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });

        var (payment, refund) = await service.RefundAsync(args.OrderId, args.BuyerId, args.Body.Amount, args.Body.IdempotencyKey);

        return Results.Created($"api/orders/{args.OrderId}/refunds/{refund.Id}", new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Payment = PaymentStateDto.From(payment)
        });
    }
}
