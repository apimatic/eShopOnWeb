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

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore]
    public string CallerUsername { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>Returns the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                return await HandleAsync(new MyOrdersRequest { CallerUsername = CallerIdentity.RequireUsername(user) }, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service)
    {
        var orders = await service.GetMyOrdersAsync(request.CallerUsername);
        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(OrderSummaryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
