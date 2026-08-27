using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    /// <summary>Populated from the JWT; never read from the request.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) {}
    public MyOrdersResponse() {}

    public List<OrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity?.Name }, orderPaymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService orderPaymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderPaymentService.GetOrdersForBuyerAsync(request.BuyerId);

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(OrderMapper.ToDto).ToList()
        };
        return Results.Ok(response);
    }
}
