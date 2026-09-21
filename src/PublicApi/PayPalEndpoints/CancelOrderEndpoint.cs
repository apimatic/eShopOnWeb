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

public class CancelOrderRequest : BaseRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action (admin): cancels an order before fulfilment, voiding the hold so the shopper's held
/// funds are released and no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId, Ct = ct }, service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService service)
    {
        var payment = await service.CancelOrderAsync(request.OrderId, request.Ct);
        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString()
        };
        return Results.Ok(response);
    }
}
