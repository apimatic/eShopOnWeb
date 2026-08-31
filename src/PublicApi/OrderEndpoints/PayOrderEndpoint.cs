using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total via PayPal. Money is not taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, ClaimsPrincipal, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, int orderId, [FromBody] PayOrderRequest request, IOrderPaymentService orderService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(user, request, orderService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, PayOrderRequest request, IOrderPaymentService orderService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var payment = await orderService.PayOrderAsync(
            buyerId, request.OrderId, Map(request.Card), request.PaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            Status = "PaymentAuthorized",
            AuthorizationId = payment.AuthorizationId ?? string.Empty,
            AuthorizationStatus = payment.AuthorizationStatus ?? string.Empty,
            AuthorizedAmount = payment.AuthorizedAmount,
            Currency = payment.Currency,
            AuthorizationExpiresAt = payment.AuthorizationExpiration
        };

        return Results.Ok(response);
    }

    internal static CardDetails? Map(CardDetailsDto? dto)
    {
        if (dto == null)
        {
            return null;
        }
        return new CardDetails
        {
            Number = dto.Number,
            Expiry = dto.Expiry,
            SecurityCode = dto.SecurityCode,
            Name = dto.Name,
            BillingAddress = dto.BillingAddress == null ? null : new CardBillingAddress
            {
                AddressLine1 = dto.BillingAddress.AddressLine1,
                AdminArea2 = dto.BillingAddress.City,
                AdminArea1 = dto.BillingAddress.State,
                PostalCode = dto.BillingAddress.PostalCode,
                CountryCode = dto.BillingAddress.CountryCode
            }
        };
    }
}
