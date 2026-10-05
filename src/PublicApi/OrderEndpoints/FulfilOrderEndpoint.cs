using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderActionRequest : BaseRequest
{
    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures (takes) the authorized payment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService service) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), service);
            })
            .Produces<PaymentActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, PaymentService service)
    {
        var result = await service.FulfilAsync(request.OrderId, CancellationToken.None);
        return Results.Ok(PaymentActionResponses.From(request, result));
    }
}

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing any held funds (no money moves).
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService service) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), service);
            })
            .Produces<PaymentActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, PaymentService service)
    {
        var result = await service.CancelAsync(request.OrderId, CancellationToken.None);
        return Results.Ok(PaymentActionResponses.From(request, result));
    }
}
