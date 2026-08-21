using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _paymentSettings;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings paymentSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new EmptyRequest(), paymentService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IOrderPaymentService paymentService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = Caller.Name(httpContext);
        var orders = await paymentService.ListBuyerOrdersAsync(buyerId, httpContext.RequestAborted);
        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => OrderDto.From(o, _paymentSettings.Currency)).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
