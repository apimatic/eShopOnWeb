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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ISavedPaymentMethodService service, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        if (request.Card == null)
        {
            throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.OrderPaymentException(400, "Card details are required.");
        }

        var saved = await service.SaveAsync(request.BuyerId, request.Card.ToInput());
        var dto = PaymentMethodDto.From(saved);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            PaymentMethod = dto
        });
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardPaymentRequest Card { get; set; } = new();
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
