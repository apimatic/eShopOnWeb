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
/// Operator: cancels the order before fulfilment, releasing the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderService, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderService, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService orderService)
    {
        return HandleAsync(request, orderService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService orderService, CancellationToken ct)
    {
        try
        {
            var order = await orderService.CancelOrderAsync(request.OrderId, ct);

            var response = new CancelOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                PaymentStatus = order.PaymentStatus.ToString(),
                AuthorizationId = order.AuthorizationId,
                AuthorizationStatus = order.AuthorizationStatus
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}
