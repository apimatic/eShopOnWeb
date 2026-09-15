using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Request carrying just the order id, for the operator lifecycle actions with no body.</summary>
public class OrderOperationRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: fulfil an order — capture (take) the held funds. Renews a stale hold first if needed.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service, ct);
            })
            .Produces<OrderPaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(OrderOperationRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IPaymentService service, CancellationToken ct)
    {
        var payment = await service.FulfilOrderAsync(request.OrderId, ct);
        return Results.Ok(OrderPaymentDto.From(payment));
    }
}
