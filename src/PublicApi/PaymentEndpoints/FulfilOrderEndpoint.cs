using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized payment — this is when the
/// money is actually taken. A stale authorization is renewed first rather than failing the fulfilment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), paymentService);
            })
            .Produces<PaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.FulfilOrderAsync(request.OrderId);
        return Results.Ok(PaymentResponse.From(result));
    }
}

public class FulfilOrderRequest
{
    public FulfilOrderRequest(int orderId) => OrderId = orderId;
    public int OrderId { get; }
}
