using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts AwaitingPayment —
/// pay it with POST /api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, BuyerContext<IOrderPaymentService>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var context = new BuyerContext<IOrderPaymentService>(user.Identity!.Name!, orderPaymentService);
                return await HandleAsync(request, context);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, BuyerContext<IOrderPaymentService> context)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var items = request.Items.Select(i => new OrderLineItemRequest(i.CatalogItemId, i.Quantity)).ToList();

        Order order;
        try
        {
            order = await context.Service.PlaceOrderAsync(context.BuyerId, address, items, default);
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        response.OrderId = order.Id;
        response.Order = OrderDto.FromOrder(order);
        return Results.Created($"api/my-orders", response);
    }
}
