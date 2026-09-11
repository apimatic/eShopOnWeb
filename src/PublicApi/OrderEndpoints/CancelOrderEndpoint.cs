using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public CancelOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new OrderActionRequest { OrderId = orderId }, paymentService))
            .Produces<PaymentActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentService paymentService)
    {
        var payment = await paymentService.CancelAsync(request.OrderId, _http.HttpContext!.RequestAborted);
        return Results.Ok(new PaymentActionResponse() { Payment = payment.ToDto() });
    }
}
