using System;
using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderService, CancellationToken ct) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(user.Identity?.Name ?? string.Empty), orderService, ct);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService orderService)
    {
        return HandleAsync(request, orderService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService orderService, CancellationToken ct)
    {
        try
        {
            var orders = await orderService.GetMyOrdersAsync(request.BuyerId, ct);

            var response = new ListMyOrdersResponse(request.CorrelationId())
            {
                Orders = orders.Select(OrderDto.FromOrder).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public ListMyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}
