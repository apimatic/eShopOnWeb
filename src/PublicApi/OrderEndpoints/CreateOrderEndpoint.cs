using System.Linq;
using System.Threading;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICatalogOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, ICatalogOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, service, httpContext, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICatalogOrderService service)
        => HandleAsync(request, service, null!, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        ICatalogOrderService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var buyerId = EndpointIdentity.GetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var address = new Address(
            string.IsNullOrWhiteSpace(request.Street) ? "123 Main St." : request.Street,
            string.IsNullOrWhiteSpace(request.City) ? "Kent" : request.City,
            string.IsNullOrWhiteSpace(request.State) ? "OH" : request.State,
            string.IsNullOrWhiteSpace(request.Country) ? "United States" : request.Country,
            string.IsNullOrWhiteSpace(request.ZipCode) ? "44240" : request.ZipCode);

        var items = (request.Items ?? new()).Select(i => new CatalogOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceAsync(buyerId, items, address, ct);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Status = order.FulfillmentStatus.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
