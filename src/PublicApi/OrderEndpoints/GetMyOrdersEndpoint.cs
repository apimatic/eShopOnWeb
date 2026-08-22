using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    private readonly PayPalSettings _payPalSettings;

    public GetMyOrdersEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, service, cancellationToken);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService orderPaymentService)
        => HandleAsync(user, orderPaymentService, CancellationToken.None);

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken)
    {
        var buyerId = CreateOrderEndpoint.RequireBuyerId(user);
        var orders = await orderPaymentService.GetMyOrdersAsync(buyerId, cancellationToken);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.ToDto(o, _payPalSettings.Currency)).ToList()
        });
    }
}
