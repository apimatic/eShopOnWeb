using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The eShopOnWeb user whose Maxio customer record is being managed.
/// Built from the JWT-authenticated caller's identity.
/// </summary>
/// <param name="UserId">The stable eShopOnWeb user id (used as the Maxio customer reference key).</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">A display first name.</param>
/// <param name="LastName">A display last name.</param>
public sealed record MaxioUserInfo(string UserId, string Email, string FirstName, string LastName);
