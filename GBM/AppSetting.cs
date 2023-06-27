

using GBM.Model;
using Microsoft.Identity.Client;
using System.Net;

namespace PartnerLed
{
    internal class AppSetting
    {
        public AppSetting() => init();

        public IPublicClientApplication InteractiveApp { get; set; }

        public string GdapBaseEndPoint { get; private set; }

        public string MicrosoftGraphBaseEndpoint { get; private set; }

        public string PartnerCenterAPI { get; private set; }

        public CustomProperties customProperties { get; private set; }

        

        private HttpClientHandler httpHandler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.All
        };

        public HttpClient Client { get { return new HttpClient(httpHandler); } }

        private void init()
        {
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            AppSettingsConfiguration config;

            if (string.IsNullOrEmpty(env))
            {
                config = AppSettingsConfiguration.ReadFromJsonFile($"appsettings.json");
            }
            else
            {
                config = AppSettingsConfiguration.ReadFromJsonFile($"appsettings.{env}.json");
            }

            var appConfig = config.PublicClientApplicationOptions;
            GdapBaseEndPoint = config.GdapEndPoint;
            MicrosoftGraphBaseEndpoint = config.MicrosoftGraphBaseEndpoint;
            PartnerCenterAPI= config.PartnerCenterAPI;
            customProperties = config.customProperties;
            InteractiveApp = PublicClientApplicationBuilder
                        .CreateWithApplicationOptions(appConfig)
                        .WithDefaultRedirectUri().WithExtraQueryParameters(new Dictionary<string, string>(){ { "acr_values", "urn:microsoft:policies:mfa" } }).Build();
        }
    }
}
