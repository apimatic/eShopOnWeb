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
/// Operator action: fulfils the order and captures the held funds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderService) =>
            {
                return await HandleAsync(orderId, orderService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService orderService)
    {
        var payment = await orderService.FulfilOrderAsync(orderId);

        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            Status = "Fulfilled",
            CaptureId = payment.CaptureId ?? string.Empty,
            CaptureStatus = payment.CaptureStatus ?? string.Empty,
            CapturedAmount = payment.CapturedAmount ?? 0m,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            Currency = payment.Currency,
            CapturedAt = payment.CapturedAt
        });
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
    public string CaptureStatus { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CapturedAt { get; set; }
}
