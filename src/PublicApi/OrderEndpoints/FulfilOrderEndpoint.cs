using System.Text.Json.Serialization;
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
    public OrderActionRequest(int orderId, CancellationToken requestAborted)
    {
        OrderId = orderId;
        RequestAborted = requestAborted;
    }

    public int OrderId { get; }
    [JsonIgnore] public CancellationToken RequestAborted { get; }
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds. A stale authorization is renewed
/// first; one that can no longer be renewed is reported with what the operator should do.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, PaymentService>
{
    private readonly PaymentSettings _paymentSettings;

    public FulfilOrderEndpoint(PaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService paymentService, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId, ct), paymentService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, PaymentService paymentService)
    {
        await paymentService.FulfilAsync(request.OrderId, request.RequestAborted);
        return Results.Ok(await OrderResults.BuildAsync(request.CorrelationId(), request.OrderId, paymentService, _paymentSettings, request.RequestAborted));
    }
}

/// <summary>Operator action: cancels an unfulfilled order and releases the shopper's held funds.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, PaymentService>
{
    private readonly PaymentSettings _paymentSettings;

    public CancelOrderEndpoint(PaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService paymentService, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId, ct), paymentService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, PaymentService paymentService)
    {
        await paymentService.CancelAsync(request.OrderId, request.RequestAborted);
        return Results.Ok(await OrderResults.BuildAsync(request.CorrelationId(), request.OrderId, paymentService, _paymentSettings, request.RequestAborted));
    }
}
