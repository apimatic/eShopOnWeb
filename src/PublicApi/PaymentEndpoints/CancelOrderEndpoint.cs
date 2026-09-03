using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: cancel before fulfilment — release the shopper's held funds, so no money moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public CancelOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService service) => await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service))
            .Produces<OrderPaymentView>()
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService service)
    {
        var view = await service.CancelAsync(request.OrderId, _http.HttpContext!.RequestAborted);
        return Results.Ok(view);
    }
}
