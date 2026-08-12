using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider
/// and stored in its canonical form; an unusable destination is rejected here.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IContactNumberService contactNumberService, HttpContext http) =>
            {
                return await HandleAsync(request, contactNumberService, http);
            })
            .Produces<CreateContactNumberResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateContactNumberResponse(request.CorrelationId());
        var result = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, http.RequestAborted);

        if (!result.Success || result.ContactNumber is null)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        response.ContactNumberId = result.ContactNumber.Id;
        response.ContactNumber = ContactNumberDto.From(result.ContactNumber);
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public ContactNumberDto ContactNumber { get; set; } = new();
}
