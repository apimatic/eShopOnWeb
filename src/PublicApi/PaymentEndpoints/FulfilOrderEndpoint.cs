using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: fulfils the order and captures the money. A stale hold is renewed first;
/// one that can no longer be renewed fails with an operator-actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service);
            })
            .Produces<PaymentDto>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
    {
        var payment = await service.FulfilAsync(request.OrderId);
        return Results.Ok(PaymentDto.From(OrderStatus.Fulfilled, payment));
    }
}
