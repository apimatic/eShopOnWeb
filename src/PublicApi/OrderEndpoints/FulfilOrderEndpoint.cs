using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

/// <summary>
/// Operator action: marks an order fulfilled, which captures its held authorization. Stale
/// authorizations are renewed automatically when still within PayPal's reauthorization window.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), orderPaymentService);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        try
        {
            var order = await orderPaymentService.FulfilAsync(request.OrderId, default);
            return order is null ? Results.NotFound() : Results.Ok(OrderDto.FromOrder(order));
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(ex.Message);
        }
        catch (ReauthorizationNotPossibleException ex)
        {
            return Results.Conflict(ex.Message);
        }
    }
}
