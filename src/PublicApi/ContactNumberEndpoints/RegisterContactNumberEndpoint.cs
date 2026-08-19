using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates and
/// canonicalises the number; an unusable destination is rejected here (HTTP 400).
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service, System.Security.Claims.ClaimsPrincipal user) =>
            {
                request.CallerBuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.CallerBuyerId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest("A phone number is required.");

        var contactNumber = await service.RegisterAsync(request.CallerBuyerId, request.PhoneNumber);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedDate = contactNumber.CreatedDate
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The number to register, in any format the provider can canonicalise.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the JWT, never from the request body.</summary>
    [JsonIgnore]
    public string? CallerBuyerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Identifier of the newly-registered number.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    public DateTimeOffset CreatedDate { get; set; }
}
