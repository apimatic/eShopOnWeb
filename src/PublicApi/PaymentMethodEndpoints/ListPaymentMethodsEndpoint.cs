using System.Collections.Generic;
using System.Security.Claims;
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

/// <summary>
/// Lists the caller's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { Username = OrderMapping.GetUserName(user) }, paymentMethodService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.Username))
        {
            return Results.Unauthorized();
        }

        var methods = await paymentMethodService.ListAsync(request.Username);
        var response = new ListPaymentMethodsResponse();
        foreach (var method in methods)
        {
            response.PaymentMethods.Add(SavePaymentMethodEndpoint.ToResponse(method));
        }
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string? Username { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavePaymentMethodResponse> PaymentMethods { get; set; } = new();
}
