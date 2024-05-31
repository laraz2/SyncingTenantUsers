using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace SyncingTenantUsers
{
    public interface IAccountServices
    {
        Task<IActionResult> GetAccounts(IConfiguration config);
        Task<string> AcquireAccessToken(string clientId, string clientSecret, string tokenEndpointUrl);

        Task<IActionResult> SyncAccountById(string accountGuid, IConfiguration config);




    }
}
