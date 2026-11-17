using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext http, CreateOrderRequest request, PaymentService payments) =>
                {
                    request.BuyerId = http.User.Identity?.Name ?? string.Empty;
                    request.RequestAborted = http.RequestAborted;
                    return await HandleAsync(request, payments);
                })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var (orderId, payment) = await payments.CreateOrderAsync(
            request.BuyerId, request.Items, request.ShippingAddress, request.RequestAborted);

        return Results.Created($"api/orders/{orderId}", new CreateOrderResponse
        {
            OrderId = orderId,
            Payment = payment
        });
    }
}

public class CreateOrderRequest : PaymentRequestBase
{
    public List<OrderItemInput> Items { get; set; } = new();
    public AddressInput? ShippingAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderPaymentView Payment { get; set; } = null!;
}
