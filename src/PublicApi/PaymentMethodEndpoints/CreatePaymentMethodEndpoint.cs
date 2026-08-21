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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
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
            (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = Caller.Name(httpContext);
        var method = await paymentMethodService.SaveCardAsync(
            buyerId,
            request.Card.ToCardDetails(),
            request.Alias,
            httpContext.RequestAborted);

        var dto = Map(method);
        return Results.Created($"api/payment-methods/{method.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            PaymentMethod = dto
        });
    }

    internal static PaymentMethodDto Map(ApplicationCore.Entities.BuyerAggregate.PaymentMethod method)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = method.Id,
            Last4 = method.Last4,
            Brand = method.Brand,
            Expiry = method.Expiry,
            Alias = method.Alias
        };
    }
}
