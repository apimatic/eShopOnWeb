using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var items = request.Items.ConvertAll(i => new PlaceOrderItem
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        });

        PlaceOrderAddress? shipTo = request.ShipTo == null
            ? null
            : new PlaceOrderAddress
            {
                Street = request.ShipTo.Street ?? string.Empty,
                City = request.ShipTo.City ?? string.Empty,
                State = request.ShipTo.State ?? string.Empty,
                Country = request.ShipTo.Country ?? string.Empty,
                ZipCode = request.ShipTo.ZipCode ?? string.Empty
            };

        var result = await service.PlaceOrderAsync(user.RequireBuyerId(), items, shipTo);
        var response = OrderResponse.From(result, request.CorrelationId());
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
