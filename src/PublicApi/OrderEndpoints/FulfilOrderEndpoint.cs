using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record FulfilOrderResponse(string CaptureId, string Status, string? CapturedAmount, string? PayPalFee, string? NetAmount);

public record FulfilOrderRequest(int OrderId);

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FulfilOrderEndpoint(IPayPalPaymentService payPal, IHttpContextAccessor httpContextAccessor)
    {
        _payPal = payPal;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), orderRepo);
            })
            .Produces<FulfilOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> orderRepo)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        var spec = new OrderWithPaymentByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
        if (order == null) return Results.NotFound();
        if (order.PaymentStatus != PaymentStatus.Authorized)
            return Results.BadRequest($"Order is in state {order.PaymentStatus} and cannot be fulfilled.");

        var authId = order.AuthorizationId!;
        var idempotencyKey = $"capture-{order.Id}";

        try
        {
            // Renew if within 1 day of expiry or already expired
            if (order.AuthorizationExpiresAt != null &&
                DateTimeOffset.TryParse(order.AuthorizationExpiresAt, out var expiresAt) &&
                expiresAt <= DateTimeOffset.UtcNow.AddDays(1))
            {
                var reauth = await _payPal.ReauthorizeAsync(authId, $"reauth-{order.Id}", ct);
                order.UpdateAuthorization(reauth.AuthorizationId, reauth.ExpiresAt);
                authId = reauth.AuthorizationId;
            }

            var capture = await _payPal.CaptureAuthorizationAsync(authId, idempotencyKey, ct);
            order.Fulfil(capture.CaptureId, capture.Amount, capture.PayPalFee, capture.NetAmount);
            await orderRepo.UpdateAsync(order, ct);

            return Results.Ok(new FulfilOrderResponse(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount));
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
