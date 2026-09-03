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

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: fulfil the order — capture the held funds (this is when the money is taken).
/// A stale authorization is renewed first rather than failing the fulfilment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public FulfilOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService service) => await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service))
            .Produces<OrderPaymentView>()
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService service)
    {
        var view = await service.FulfilAsync(request.OrderId, _http.HttpContext!.RequestAborted);
        return Results.Ok(view);
    }
}
