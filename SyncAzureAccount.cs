using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SyncingTenantUsers;
using System;
using System.Threading.Tasks;

public class SyncAzureAccount
{
    private readonly ILogger<SyncAzureAccount> _logger;
    private readonly IAccountServices _accountServices;

    public SyncAzureAccount(ILogger<SyncAzureAccount> logger, IAccountServices accountServices)
    {
        _logger = logger;
        _accountServices = accountServices;
    }

    [FunctionName("SyncAzureAccount")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, Microsoft.Azure.WebJobs.ExecutionContext context, // Change the ExecutionContext namespace
        ILogger log)

    {
        //_logger.LogInformation("C# HTTP trigger function processed a request.");

        string responseMessage = "This HTTP triggered function executed successfully.";

        try
        {
            // Your custom logic here to get accounts
            var result = await _accountServices.GetAccounts(context);
            // var result = await _accountServices.GetAccounts(context);

            // Replace the response message with your actual response
            responseMessage = JsonConvert.SerializeObject(result);
        }
        catch (Exception ex)
        {
            // _logger.LogError(ex, "An error occurred while processing the request.");
            Console.WriteLine(ex.ToString());
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new OkObjectResult(responseMessage);
    }
}
