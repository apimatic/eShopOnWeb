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
    public FulfilOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; set; }
}

public class OrderPaymentResponse : BaseResponse
{
    public OrderPaymentResponse(Guid correlationId) : base(correlationId) { }
    public OrderPaymentResponse() { }
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds — that is when the money is taken.
/// A stale authorization is renewed rather than failing the fulfilment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
    {
        var payment = await service.FulfilAsync(request.OrderId);
        return Results.Ok(new OrderPaymentResponse(request.CorrelationId())
        {
            Payment = PaymentStateDto.From(payment)
        });
    }
}
