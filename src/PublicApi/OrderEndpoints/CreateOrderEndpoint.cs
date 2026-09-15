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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(request, user, orderService, notifications);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
        => HandleAsync(request, new ClaimsPrincipal(), orderService, notifications: null!);

    private async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        ClaimsPrincipal user,
        IOrderService orderService,
        IOrderNotificationService notifications)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must include at least one catalog item.");
        }

        var shipTo = request.ShipTo ?? new CreateOrderAddressRequest();
        var address = new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
        var lines = request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList();
        var order = await orderService.CreateCatalogOrderAsync(user.GetBuyerId(), lines, address);

        await notifications.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
