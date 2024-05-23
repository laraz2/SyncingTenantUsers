using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace SyncingTenantUsers
{
    public interface IAccountServices
    {
        Task<IActionResult> GetAccounts(Microsoft.Azure.WebJobs.ExecutionContext context);
        Task<string> AcquireAccessToken(string clientId, string clientSecret, string tokenEndpointUrl, Microsoft.Azure.WebJobs.ExecutionContext context);
       

    }
}
