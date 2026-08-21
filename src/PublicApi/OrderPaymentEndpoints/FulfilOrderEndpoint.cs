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

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public OrderPaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: marks an order fulfilled, which is when the held money is actually taken
/// (captured). A stale hold is renewed rather than failing the fulfilment. Administrator only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service, ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var payment = await service.FulfilAsync(request.OrderId, ct);
        return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
        {
            Payment = PaymentDtoMapper.ToDto(payment)
        });
    }
}
