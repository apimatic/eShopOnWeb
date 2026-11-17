using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Services.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersEndpoint.MyOrdersRequest>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IOrderPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
                await HandleAsync(new MyOrdersRequest()))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name
            ?? throw new PaymentValidationException("A signed-in shopper is required.");

        var orders = await _paymentService.GetMyOrdersAsync(
            buyerId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

        return Results.Ok(new MyOrdersResponse { Orders = orders });
    }

    public class MyOrdersRequest
    {
    }

    public class MyOrdersResponse
    {
        public IReadOnlyList<OrderView> Orders { get; set; } = new List<OrderView>();
    }
}
