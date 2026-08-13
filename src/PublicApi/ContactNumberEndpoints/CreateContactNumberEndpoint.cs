using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    /// <summary>The id of the registered number, so the caller can drive the rest of the flow.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number, which is what gets stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here (400) rather than when a later message fails to go out, and
/// what gets stored is the provider's canonical form of the number.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                    return Results.BadRequest("A phone number is required.");

                // An invalid number throws InvalidPhoneNumberException, mapped to 400 by the middleware.
                var contactNumber = await service.RegisterAsync(ownerId, request.PhoneNumber);

                var response = new CreateContactNumberResponse(request.CorrelationId())
                {
                    ContactNumberId = contactNumber.Id,
                    PhoneNumber = contactNumber.PhoneNumber
                };
                return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
