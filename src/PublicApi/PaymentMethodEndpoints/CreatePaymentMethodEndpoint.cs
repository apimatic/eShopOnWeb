using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(request, paymentMethods);
            })
            .Produces<PaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods)
    {
        var http = _httpContextAccessor.HttpContext!;
        if (request.Card is null)
        {
            throw new CheckoutException(400, "Card details are required.");
        }

        var card = OrderDtoMapper.ToCardInput(new OrderEndpoints.CardDto
        {
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            Name = request.Card.Name,
            BillingAddress = request.Card.BillingAddress
        });

        var saved = await paymentMethods.SaveAsync(http.RequireBuyerId(), card, http.RequestAborted);
        var response = PaymentMethodMapper.ToDto(saved);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}
