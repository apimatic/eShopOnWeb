using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>, not both.</summary>
    public CardDto? Card { get; set; }

    /// <summary>A saved card id to pay with instead of raw card details.</summary>
    public Guid? SavedPaymentMethodId { get; set; }
}

/// <summary>POST /api/orders/{orderId}/pay — authorizes (holds) the order total. Does not capture.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PayOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService service) =>
                await HandleAsync(orderId, request, service))
            .WithTags("PaymentOrderEndpoints");
    }

    private Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderPaymentService service) =>
        PaymentEndpointSupport.ExecuteAsync(async () =>
        {
            var buyerId = PaymentEndpointSupport.RequireUserName(_http);
            var ct = PaymentEndpointSupport.RequestAborted(_http);
            var card = PaymentEndpointSupport.ToCardDetails(request.Card);
            var view = await service.PayAsync(buyerId, orderId, card, request.SavedPaymentMethodId, ct);
            return Results.Ok(view);
        });

    // Interface requirement — the real handler binds the {orderId} route value.
    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(0, request, service);
}
