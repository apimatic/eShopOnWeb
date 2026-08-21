using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>The signed-in shopper's own orders, each with its payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { CallerId = user.GetUserName() }, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var orders = await service.GetMyOrdersAsync(request.CallerId, ct);
        return Results.Ok(new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(PaymentDtoMapper.ToDto).ToList()
        });
    }
}
