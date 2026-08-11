using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds. A stale authorization is
/// renewed first; one that can no longer be renewed returns an operator-actionable error. Admin only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext context) => await HandleAsync(new FulfilOrderRequest(orderId), context))
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, HttpContext context)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());
        var paymentService = context.RequestServices.GetRequiredService<IPaymentService>();

        var payment = await paymentService.FulfilAsync(request.OrderId);

        response.OrderId = payment.OrderId;
        response.Status = payment.Status.ToString();
        response.CaptureId = payment.CaptureId;
        response.CaptureStatus = payment.CaptureStatus;
        response.CapturedAmount = payment.CapturedAmount;
        response.PayPalFee = payment.PayPalFee;
        response.NetAmount = payment.NetAmount;
        response.Currency = payment.CurrencyCode;

        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; init; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
