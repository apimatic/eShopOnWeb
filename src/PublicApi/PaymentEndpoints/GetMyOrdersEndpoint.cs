using System.Security.Claims;
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

public class GetMyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public GetMyOrdersEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new MyOrdersRequest(), user, cancellationToken))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        MyOrdersRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var orders = await _paymentService.GetMyOrdersAsync(buyerId, cancellationToken);

        var response = new MyOrdersResponse();
        response.Orders.AddRange(orders);
        return Results.Ok(response);
    }
}

public class MyOrdersRequest : BaseRequest
{
}
