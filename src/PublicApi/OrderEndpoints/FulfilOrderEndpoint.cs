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

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) {}
    public FulfilOrderResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized funds. If the
/// authorization has gone stale it is renewed first; if it cannot be renewed the order is
/// sent back to awaiting payment and the error says so.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orderPaymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var order = await orderPaymentService.FulfilAsync(request.OrderId);

        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            CaptureId = order.PayPalCaptureId,
            CapturedAmount = order.CapturedAmount,
            PayPalFee = order.PayPalFee,
            NetAmount = order.NetAmount,
            Currency = order.Currency
        };
        return Results.Ok(response);
    }
}
