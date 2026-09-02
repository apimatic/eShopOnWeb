using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized funds.
/// Renews a stale authorization when PayPal still allows it.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderPaymentService _paymentService;

    public FulfilOrderEndpoint(IOrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        try
        {
            var payment = await _paymentService.FulfilAsync(orderId);

            return Results.Ok(new FulfilOrderResponse
            {
                OrderId = orderId,
                Status = payment.Status.ToString(),
                CaptureId = payment.CaptureId,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                Currency = payment.Currency,
                CapturedAt = payment.CapturedAt
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CapturedAt { get; set; }
}
