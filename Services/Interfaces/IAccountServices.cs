
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using SyncingTenantUsers.Models.Contacts;
using SyncingTenantUsers.Models.Accounts;
using System.Collections.Generic;
using System.Net.Http;

namespace SyncingTenantUsers
{
    public interface IAccountServices
    {
        Task<IActionResult> GetAccounts();
        Task<string> AcquireAccessToken(string clientId, string clientSecret, string tokenEndpointUrl);
       

    }
}
