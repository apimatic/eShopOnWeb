using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Authorizes (holds) the order total, via a one-off card or a saved card. Does not take the money.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CallerIdentity.BuyerId(http);
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<PaymentStateResponse>()
            .WithTags("PayPalPayments");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        CardDetails? card = request.Card?.ToCardDetails();
        var view = await service.PayAsync(request.BuyerId, request.OrderId, card, request.PaymentMethodId, request.Ct);
        return Results.Ok(PaymentMapper.ToResponse(view, request.CorrelationId()));
    }
}
