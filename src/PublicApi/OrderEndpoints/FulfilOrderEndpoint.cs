using System;
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
/// Operator: fulfils the order — this is when the held money is actually captured. A stale
/// authorization is renewed first; one that can no longer be renewed gets an actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderService, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), orderService, ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService orderService)
    {
        return HandleAsync(request, orderService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService orderService, CancellationToken ct)
    {
        try
        {
            var order = await orderService.FulfilOrderAsync(request.OrderId, ct);

            var response = new FulfilOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                PaymentStatus = order.PaymentStatus.ToString(),
                CaptureId = order.CaptureId,
                CapturedAmount = order.CapturedGrossAmount,
                PayPalFee = order.CapturedFeeAmount,
                NetAmount = order.CapturedNetAmount,
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
