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
/// Operator action: cancels an order before fulfilment, releasing the held funds so no money ever moved.
/// Admin only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext context) => await HandleAsync(new CancelOrderRequest(orderId), context))
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, HttpContext context)
    {
        var response = new CancelOrderResponse(request.CorrelationId());
        var paymentService = context.RequestServices.GetRequiredService<IPaymentService>();

        var payment = await paymentService.CancelAsync(request.OrderId);

        response.OrderId = payment.OrderId;
        response.Status = payment.Status.ToString();

        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public CancelOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; init; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
