using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PlaceOrderItemDto(int CatalogItemId, int Quantity);

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemDto> Items { get; set; } = new();
    public string Street { get; set; } = "123 Main St";
    public string City { get; set; } = "Anytown";
    public string State { get; set; } = "WA";
    public string Country { get; set; } = "US";
    public string ZipCode { get; set; } = "98101";
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
}

public class PlaceOrderEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IRepository<Order> orderRepo,
                   IRepository<CatalogItem> catalogRepo, HttpContext httpContext, CancellationToken ct) =>
            {
                var buyerId = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (request.Items == null || !request.Items.Any())
                    return Results.BadRequest("At least one item is required.");

                var itemIds = request.Items.Select(i => i.CatalogItemId).ToArray();
                var catalogItems = await catalogRepo.ListAsync(new CatalogItemsSpecification(itemIds), ct);

                var orderItems = new List<OrderItem>();
                foreach (var line in request.Items)
                {
                    var cat = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
                    if (cat == null) return Results.BadRequest($"Catalog item {line.CatalogItemId} not found.");
                    orderItems.Add(new OrderItem(
                        new CatalogItemOrdered(cat.Id, cat.Name, cat.PictureUri ?? ""),
                        cat.Price, line.Quantity));
                }

                var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
                var order = new Order(buyerId, address, orderItems);
                await orderRepo.AddAsync(order, ct);

                var response = new PlaceOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Status = order.Status.ToString()
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<PlaceOrderResponse>(201)
            .Produces(400)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.StatusCode(501));
}
