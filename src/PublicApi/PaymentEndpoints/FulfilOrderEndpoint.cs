using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the money. Renews a stale authorization
/// rather than failing. Restricted to administrators. POST /api/orders/{orderId}/fulfil
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderPaymentService _service;

    public FulfilOrderEndpoint(IOrderPaymentService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var result = await _service.FulfilAsync(orderId);

        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = orderId,
            PaymentStatus = PaymentStatus.Captured.ToString(),
            CaptureId = result.CaptureId,
            CapturedAmount = result.CapturedAmount,
            PayPalFee = result.PayPalFee,
            NetAmount = result.NetAmount,
            Currency = result.Currency
        });
    }
}
