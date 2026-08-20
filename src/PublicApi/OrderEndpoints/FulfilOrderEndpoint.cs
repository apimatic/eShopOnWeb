using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    internal int OrderId { get; set; }
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public FulfilOrderEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, ICheckoutService checkout) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, checkout);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ICheckoutService checkout)
    {
        var order = await checkout.FulfilAsync(request.OrderId);
        return Results.Ok(OrderResponse.From(order, _paymentSettings.Currency));
    }
}
