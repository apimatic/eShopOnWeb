using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(string message, HttpStatusCode statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }
}

public class MaxioDuplicateSubmissionException : MaxioApiException
{
    public MaxioDuplicateSubmissionException()
        : base("Maxio rejected a duplicate submission.", HttpStatusCode.Conflict)
    {
    }
}

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
