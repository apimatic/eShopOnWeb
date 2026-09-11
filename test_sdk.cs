using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().AddUserSecrets("7fe53add-ebed-4130-aed2-2ac8fc92c8c8").Build();
var opts = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = config["Maxio:ApiKey"], Password = "x" },
    Environment = ServerEnvironment.Us
};
opts.Server.Production.Us.BaseUrl = $"https://{config["Maxio:Subdomain"]}.chargify.com";
using var hc = new System.Net.Http.HttpClient();
try {
    var client = new MaxioAdvancedBillingClient(hc, opts);
    var families = client.ProductFamilies.ListProductFamilies(ct: default);
    Console.WriteLine("OK: families count=" + families.Count);
} catch (Exception ex) {
    Console.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
    if (ex.InnerException != null) Console.WriteLine("INNER: " + ex.InnerException.Message);
}
