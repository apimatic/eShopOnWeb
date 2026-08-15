using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public PaymentStateDto? Payment { get; set; }
}

/// <summary>Marks an order fulfilled, capturing the held funds. Operator (administrator) action.</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.FulfilAsync(request.OrderId);
        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = PaymentStateDto.From(order.Payment)
        });
    }
}
