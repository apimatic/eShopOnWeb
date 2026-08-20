using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SaveCardRequest, IPaymentCheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SaveCardRequest request, IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(request, payments);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, IPaymentCheckoutService payments)
    {
        var method = await payments.SaveCardAsync(
            EndpointUser.BuyerId(_httpContextAccessor.HttpContext!),
            OrderResponseMapper.ToCardDetails(request.Card)!);

        var response = OrderResponseMapper.Map(method);
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}
