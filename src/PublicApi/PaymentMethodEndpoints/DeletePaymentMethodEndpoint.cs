using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes a saved card. The card is disabled at Finix and excluded from all future reads/payments.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, OrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, OrderPaymentService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderPaymentService service)
    {
        var buyerId = GetBuyerId();
        var paymentMethodId = GetRouteInt("paymentMethodId");
        var card = await service.DeleteCardAsync(buyerId, paymentMethodId, CancellationToken.None);

        var response = new DeletePaymentMethodResponse(System.Guid.NewGuid())
        {
            PaymentMethodId = card.Id,
            Status = "Deleted"
        };

        return Results.Ok(response);
    }

    private string GetBuyerId()
    {
        var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "NO_IDENTITY",
                "The access token does not contain a user identity.");
        }

        return name!;
    }

    private int GetRouteInt(string name)
    {
        var value = _httpContextAccessor.HttpContext?.GetRouteValue(name)?.ToString();
        if (!int.TryParse(value, out var result))
        {
            throw new PaymentOperationException(400, "INVALID_ROUTE", $"The route value '{name}' is invalid.");
        }

        return result;
    }
}
