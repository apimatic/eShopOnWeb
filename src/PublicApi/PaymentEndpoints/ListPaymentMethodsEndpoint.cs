using System;
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

/// <summary>Returns the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { CallerName = user.Identity?.Name }, service);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedCardService service)
    {
        if (string.IsNullOrEmpty(request.CallerName))
        {
            return Results.Unauthorized();
        }

        var methods = await service.ListAsync(request.CallerName);
        var response = new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = methods.Select(PaymentMappers.ToDto).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    [JsonIgnore]
    public string? CallerName { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}
