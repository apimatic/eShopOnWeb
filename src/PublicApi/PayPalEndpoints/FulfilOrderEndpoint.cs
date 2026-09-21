using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// Operator action (admin): marks the order fulfilled and captures the money. Renews a stale
/// authorization first; if it can no longer be renewed, returns an operator-actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId, Ct = ct }, service);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService service)
    {
        var payment = await service.FulfilOrderAsync(request.OrderId, request.Ct);
        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            CaptureId = payment.CaptureId,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            Currency = payment.Currency
        };
        return Results.Ok(response);
    }
}
