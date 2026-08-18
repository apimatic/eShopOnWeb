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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and tells them it was placed. Reuses
/// the app's existing order/order-item model.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, string, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(request, user.Identity!.Name!, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, string buyerId,
        IOrderNotificationService service)
    {
        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();

        Address? address = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var result = await service.PlaceOrderAsync(buyerId, lines, address);
        if (!result.IsSuccess)
            return result.ToFailureResult();

        var order = result.Value;
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
