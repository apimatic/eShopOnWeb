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

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                var command = new PayOrderCommand
                {
                    PaymentMethodId = request.PaymentMethodId,
                    Card = request.Card == null ? null : new CardPaymentDetails
                    {
                        Number = request.Card.Number,
                        ExpirationMonth = request.Card.ExpirationMonth,
                        ExpirationYear = request.Card.ExpirationYear,
                        SecurityCode = request.Card.SecurityCode,
                        Name = request.Card.Name,
                        Address = request.Card.Address == null ? null : new PaymentAddress
                        {
                            Line1 = request.Card.Address.Line1 ?? "123 Main St",
                            Line2 = request.Card.Address.Line2,
                            City = request.Card.Address.City ?? "San Francisco",
                            Region = request.Card.Address.Region ?? "CA",
                            PostalCode = request.Card.Address.PostalCode ?? "94105",
                            Country = request.Card.Address.Country ?? "USA"
                        }
                    }
                };

                var order = await service.PayAsync(buyerId, orderId, command, cancellationToken);
                var response = new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Order = PaymentDtoMapper.Map(order)
                };

                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
