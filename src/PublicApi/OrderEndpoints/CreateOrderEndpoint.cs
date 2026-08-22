using System;
using System.Collections.Generic;
using System.Linq;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                var userName = http.GetUserName();
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, service, userName, cancellationToken);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, string.Empty, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IOrderNotificationService service,
        string buyerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var lines = (request.Items ?? new List<CreateOrderItemRequest>())
                .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
                .ToList();
            Address? shipTo = request.ShipTo is null
                ? null
                : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

            var order = await service.PlaceOrderAsync(buyerId, lines, shipTo, cancellationToken);
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
