using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Returns the signed-in shopper's own orders with their payment state.</summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(BuyerIdentity.GetBuyerId(user)), paymentService);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IPaymentService paymentService)
    {
        var orders = await paymentService.GetMyOrdersAsync(request.BuyerId);
        return Results.Ok(new GetMyOrdersResponse { Orders = orders });
    }
}

public class GetMyOrdersRequest
{
    public GetMyOrdersRequest(string buyerId) => BuyerId = buyerId;
    public string BuyerId { get; }
}

public class GetMyOrdersResponse : BaseResponse
{
    public IReadOnlyList<OrderPaymentView> Orders { get; set; } = new List<OrderPaymentView>();
}
