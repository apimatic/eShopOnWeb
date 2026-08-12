using System;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any format the provider can understand.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the number just registered.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. The number is
/// validated with the provider up front; one it does not consider a usable destination is rejected
/// here (400). What is stored is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var result = await service.RegisterAsync(buyerId, request.PhoneNumber);
                if (result.Status == ResultStatus.Invalid)
                {
                    return Results.BadRequest(new { errors = result.ValidationErrors });
                }
                if (!result.IsSuccess)
                {
                    return Results.BadRequest();
                }

                var response = new RegisterContactNumberResponse(request.CorrelationId())
                {
                    ContactNumberId = result.Value.Id,
                    PhoneNumber = result.Value.PhoneNumber
                };
                return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
