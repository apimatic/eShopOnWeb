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

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequestDto Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodResponse PaymentMethod { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService service, HttpContext http) =>
            {
                return await HandleAsync(request, service, http);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
        => HandleAsync(request, service, http: null!);

    private async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        ISavedPaymentMethodService service,
        HttpContext http)
    {
        var saved = await service.SaveAsync(
            http.RequireBuyerId(),
            OrderResponseMapper.ToCardDetails(request.Card),
            http.RequestAborted);
        var mapped = PaymentMethodResponse.Map(saved);
        return Results.Created($"api/payment-methods/{mapped.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = mapped.PaymentMethodId,
            PaymentMethod = mapped
        });
    }
}
