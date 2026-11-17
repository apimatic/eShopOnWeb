using System;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Creates an order from catalog items for the signed-in shopper.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    throw new PaymentException(401, "A signed-in shopper is required.");
                }

                var items = (request.Items ?? new List<CreateOrderItemRequest>())
                    .Select(i => (i.CatalogItemId, i.Quantity))
                    .ToList();

                Address? address = null;
                if (request.ShippingAddress is not null)
                {
                    address = new Address(
                        request.ShippingAddress.Street ?? "Not provided",
                        request.ShippingAddress.City ?? "Not provided",
                        request.ShippingAddress.State ?? "N/A",
                        request.ShippingAddress.Country ?? "USA",
                        request.ShippingAddress.ZipCode ?? "00000");
                }

                var created = await paymentService.CreateOrderAsync(buyerId, items, address, cancellationToken);
                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = created.Order.Id,
                    OrderDate = created.Order.OrderDate,
                    Total = created.Order.Total(),
                    Currency = created.Payment?.Currency ?? string.Empty,
                    PaymentStatus = created.Payment?.Status.ToString() ?? OrderPaymentStatus.AwaitingPayment.ToString()
                };

                return Results.Created($"api/orders/{response.OrderId}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
