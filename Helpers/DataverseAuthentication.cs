using Microsoft.Identity.Client;
using System;
using System.Threading.Tasks;
namespace SyncingTenantUsers.Helpers
{
    public class DataverseAuthentication
    {
        private readonly string clientId="";
        private readonly string clientSecret="";
        private readonly string authority="";
        private readonly string resource="";
        public DataverseAuthentication() { }
        public DataverseAuthentication(string clientId, string clientSecret, string authority, string resource)
        {
           
            this.clientId = clientId;
            
            this.clientSecret = clientSecret;
            
            this.authority = authority;
            
            this.resource = resource;
        }

        public async Task<string> GetAccessToken()
        {
            var cca = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority(new Uri(authority))
                .Build();

            var result = await cca.AcquireTokenForClient(new[] { resource + "/.default" })
                .ExecuteAsync();

            return result.AccessToken;
        }
    }
}