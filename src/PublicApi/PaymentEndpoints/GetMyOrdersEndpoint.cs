using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class GetMyOrdersRequest : BaseRequest
{
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class GetMyOrdersResponse : BaseResponse
{
    public GetMyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public GetMyOrdersResponse() { }
    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>The signed-in shopper's own orders, each with its payment state.</summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetMyOrdersRequest { CallerId = PaymentEndpointHelpers.GetCallerId(user) }, service);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IOrderPaymentService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(request.CallerId);
        var response = new GetMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(OrderDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
