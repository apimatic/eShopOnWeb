using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                var command = new CreateOrderCommand
                {
                    Items = (request.Items ?? new System.Collections.Generic.List<CreateOrderItemRequest>())
                        .Select(i => new CreateOrderItem
                        {
                            CatalogItemId = i.CatalogItemId,
                            Quantity = i.Quantity
                        }).ToList(),
                    ShippingAddress = request.ShippingAddress == null ? null : new PaymentAddress
                    {
                        Line1 = request.ShippingAddress.Line1 ?? "123 Main St",
                        Line2 = request.ShippingAddress.Line2,
                        City = request.ShippingAddress.City ?? "San Francisco",
                        Region = request.ShippingAddress.Region ?? "CA",
                        PostalCode = request.ShippingAddress.PostalCode ?? "94105",
                        Country = request.ShippingAddress.Country ?? "USA"
                    }
                };

                var order = await service.CreateOrderAsync(buyerId, command, cancellationToken);
                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Order = PaymentDtoMapper.Map(order)
                };

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
