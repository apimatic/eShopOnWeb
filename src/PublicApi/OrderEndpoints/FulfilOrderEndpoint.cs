using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils a paid order, capturing the held funds. The response
/// reports what PayPal reported: captured amount, PayPal's fee and the net
/// proceeds to the merchant. A stale authorization is renewed automatically.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly PayPalSettings _payPalSettings;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService, IOptions<PayPalSettings> payPalSettings)
    {
        _orderPaymentService = orderPaymentService;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var payment = await _orderPaymentService.FulfilOrderAsync(request.OrderId, _payPalSettings.Currency);

        response.OrderId = payment.OrderId;
        response.OrderStatus = "Fulfilled";
        response.CaptureId = payment.CaptureId ?? string.Empty;
        response.CaptureStatus = payment.CaptureStatus ?? string.Empty;
        response.CapturedAmount = payment.CapturedAmount ?? 0m;
        response.PayPalFee = payment.PayPalFee;
        response.NetAmount = payment.NetAmount;
        response.Currency = payment.Currency;
        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) {}
    public FulfilOrderResponse() {}

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
    public string CaptureStatus { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
