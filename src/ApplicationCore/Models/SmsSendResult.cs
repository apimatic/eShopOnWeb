namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class SmsSendResult
{
    public bool Success { get; set; }
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }

    public static SmsSendResult Sent(string messageSid, string status) =>
        new SmsSendResult { Success = true, MessageSid = messageSid, Status = status };

    public static SmsSendResult Failed(string errorMessage) =>
        new SmsSendResult { Success = false, Status = "error", ErrorMessage = errorMessage };
}
