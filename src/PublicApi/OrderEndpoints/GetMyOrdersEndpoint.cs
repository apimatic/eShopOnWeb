using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns the signed-in shopper's own orders with their payment state.</summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, HttpContext http) =>
            {
                var request = new GetMyOrdersRequest { CallerId = http.User.Identity?.Name ?? string.Empty };
                return await HandleAsync(request, service);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IOrderPaymentService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(request.CallerId);
        var response = new GetMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(OrderPaymentDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class GetMyOrdersRequest : ShopperRequest
{
}

public class GetMyOrdersResponse : BaseResponse
{
    public GetMyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public GetMyOrdersResponse() { }

    public List<OrderPaymentDto> Orders { get; set; } = new();
}
