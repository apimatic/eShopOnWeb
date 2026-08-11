using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Save a card for the signed-in shopper. The response identifies the saved card and describes it safely
/// (brand, last four, expiry) — never full card details. POST /api/payment-methods
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService service, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (request.Card is null)
            throw new PaymentValidationException("Card details are required.");

        var response = new CreatePaymentMethodResponse(request.CorrelationId());
        var method = await service.SaveAsync(request.BuyerId!, request.Card.ToCardDetails());

        response.PaymentMethodId = method.Id;
        response.PaymentMethod = SavedPaymentMethodDto.FromEntity(method);
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    public string? BuyerId { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedPaymentMethodDto PaymentMethod { get; set; } = new();
}
