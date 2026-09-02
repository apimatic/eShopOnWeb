using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils the order and captures the previously authorized funds. A stale
/// authorization is renewed first; one that can no longer be renewed fails with an
/// actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly OrderPaymentService _paymentService;

    public FulfilOrderEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancellationToken ct) =>
            {
                return await Handle(new FulfilOrderRequest(orderId), ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request)
        => Handle(request, CancellationToken.None);

    private async Task<IResult> Handle(FulfilOrderRequest request, CancellationToken ct)
    {
        try
        {
            var (order, payment, _) = await _paymentService.FulfilAsync(request.OrderId, ct);
            return Results.Ok(new FulfilOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                CaptureId = payment.CaptureId ?? string.Empty,
                CaptureStatus = payment.CaptureStatus ?? string.Empty,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                Currency = payment.Currency
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
