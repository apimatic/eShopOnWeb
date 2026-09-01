using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total: a hold is placed on the money, nothing is taken yet.
/// Repeating the call for an already-authorized order returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderService, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderService, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderService)
    {
        return HandleAsync(request, orderService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderService, CancellationToken ct)
    {
        try
        {
            var order = await orderService.PayOrderAsync(request.BuyerId, request.OrderId,
                request.Card?.ToModel(), request.SavedCardId, ct);

            var response = new PayOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                PaymentStatus = order.PaymentStatus.ToString(),
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationId = order.AuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus,
                AuthorizationExpiresAt = order.AuthorizationExpiresAt,
                Amount = order.Total(),
                Currency = order.Currency
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}
