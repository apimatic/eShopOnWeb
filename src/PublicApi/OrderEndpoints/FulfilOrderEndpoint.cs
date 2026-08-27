using System;
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
/// Operator: marks the order fulfilled and captures the authorized funds. Renews a stale
/// authorization when PayPal still allows it.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var payment = await paymentService.FulfilOrderAsync(request.OrderId);

        response.OrderId = payment.OrderId;
        response.PaymentId = payment.PaymentId;
        response.Status = payment.Status;
        response.CaptureId = payment.CaptureId;
        response.CaptureStatus = payment.CaptureStatus;
        response.CapturedAmount = payment.CapturedAmount;
        response.PayPalFee = payment.PayPalFee;
        response.NetAmount = payment.NetAmount;
        response.Currency = payment.Currency;

        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; }

    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public FulfilOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
