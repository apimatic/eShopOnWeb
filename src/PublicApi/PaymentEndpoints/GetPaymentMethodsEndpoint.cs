using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class GetPaymentMethodsRequest : BaseRequest
{
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;
}

public class GetPaymentMethodsResponse : BaseResponse
{
    public GetPaymentMethodsResponse(System.Guid correlationId) : base(correlationId) { }
    public GetPaymentMethodsResponse() { }
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>The signed-in shopper's own saved cards.</summary>
public class GetPaymentMethodsEndpoint : IEndpoint<IResult, GetPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new GetPaymentMethodsRequest { CallerId = PaymentEndpointHelpers.GetCallerId(user) }, service);
            })
            .Produces<GetPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(GetPaymentMethodsRequest request, ISavedCardService service)
    {
        var cards = await service.GetCardsAsync(request.CallerId);
        var response = new GetPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = cards.Select(PaymentMethodDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
