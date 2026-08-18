using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; what is stored is the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService service) =>
            {
                request.CallerId = CallerIdentity.Get(httpContext) ?? string.Empty;
                return await HandleAsync(request, service);
            })
            .Produces<CreateContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        var response = new CreateContactNumberResponse(request.CorrelationId());
        try
        {
            var registration = await service.RegisterAsync(request.CallerId, request.PhoneNumber);
            if (!registration.Success)
                return Results.BadRequest(new { error = registration.Error });

            response.ContactNumberId = registration.ContactNumber!.Id;
            response.PhoneNumber = registration.ContactNumber.PhoneNumber;
            return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
        }
        catch (SmsNotificationException ex)
        {
            return ProviderErrorResults.From(ex);
        }
    }
}
