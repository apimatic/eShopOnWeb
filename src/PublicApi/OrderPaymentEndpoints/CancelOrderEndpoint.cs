using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the held funds so no money ever
/// moved. Administrator only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var payment = await service.CancelAsync(request.OrderId, ct);
        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            Payment = PaymentDtoMapper.ToDto(payment)
        });
    }
}
