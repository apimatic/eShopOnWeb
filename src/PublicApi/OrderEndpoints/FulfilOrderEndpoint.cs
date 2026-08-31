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
/// Operator action: marks the order fulfilled and captures the held funds.
/// Renews a stale authorization first. Repeating the call is safe.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var payment = await _paymentService.FulfilAsync(orderId);

        var response = new FulfilOrderResponse
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFeeAmount = payment.PayPalFeeAmount,
            NetAmount = payment.NetAmount,
            Currency = payment.Currency,
            CapturedAt = payment.CapturedAt,
            AuthorizationRenewals = payment.AuthorizationRenewals
        };
        return Results.Ok(response);
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CapturedAt { get; set; }
    public int AuthorizationRenewals { get; set; }
}
