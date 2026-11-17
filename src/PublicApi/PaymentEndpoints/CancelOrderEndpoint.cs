using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public CancelOrderEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, CancellationToken cancellationToken) =>
                    await HandleAsync(new CancelOrderRequest { OrderId = orderId }, cancellationToken))
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _paymentService.CancelOrderAsync(request.OrderId, cancellationToken);
        return Results.Ok(response);
    }
}
