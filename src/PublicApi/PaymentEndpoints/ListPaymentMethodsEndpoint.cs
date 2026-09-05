using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await paymentService.GetSavedCardsAsync(buyerId, default);

        var response = new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = cards.Select(c => new PaymentMethodDto
            {
                PaymentMethodId = c.Id,
                Brand = c.Brand,
                LastFourDigits = c.LastFourDigits,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName,
                CreatedAt = c.CreatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
}



