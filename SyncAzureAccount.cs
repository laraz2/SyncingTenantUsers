using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SyncingTenantUsers;
using SyncingTenantUsers.Helpers;
using SyncingTenantUsers.Models.Accounts;
using System;
using System.IO;
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

    [FunctionName("SyncAzureAccounts")]
    //// public async Task<IActionResult> RunSyncAzureAccounts(
    //[HttpTrigger(AuthorizationLevel.Function, "get", Route = "SyncAzureAccounts")] HttpRequest req, Microsoft.Azure.WebJobs.ExecutionContext context, // Change the ExecutionContext namespace
    //ILogger log)

    //{
    public async Task<IActionResult> RunSyncAzureAccounts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "SyncAzureAccounts")] HttpRequest req, Microsoft.Azure.WebJobs.ExecutionContext context, // Change the ExecutionContext namespace
        ILogger log)

    {
        //_logger.LogInformation("C# HTTP trigger function processed a request.");


        try
        {


            // Your custom logic here to get accounts
            var appDirectory = Directory.GetCurrentDirectory();
            var path = Path.Combine(context.FunctionAppDirectory, "appsettings.json");//{retrieves the directory path of the Azure Function App using Directory.GetCurrentDirectory()
                                                                                      //Then, it constructs the path to the appsettings.json}


            IConfigurationRoot config = new ConfigurationBuilder()//{ConfigurationBuilder is used to build a configuration object (IConfigurationRoot) by adding the appsettings.json file to it.
                                                                  //This allows the application to access configuration settings defined in the JSON file.}
                .SetBasePath(appDirectory)
                .AddJsonFile(path)
                .Build();
            var result = await _accountServices.GetAccounts(config);
            // var result = await _accountServices.GetAccounts(context);

            // Replace the response message with your actual response
            var responseMessage = JsonConvert.SerializeObject(result);

            return new OkObjectResult(result);

        }
        catch (AppException ex)
        {
            log.LogError(ex, "An application exception occurred."); // Log the exception
            return new ObjectResult(ex.AppExceptionErrorModel)
            {
                StatusCode = ex.StatusCode == int.MinValue ? 400 : ex.StatusCode  // or any other appropriate status code based on the exception
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "An unexpected exception occurred."); // Log the exception
            return new StatusCodeResult(500); // 500 Internal Server Error
        }
    }

    [FunctionName("SyncAzureAccountById")]
    public async Task<IActionResult> RunSyncAzureAccountById(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = "SyncAzureAccountById")] HttpRequest req, Microsoft.Azure.WebJobs.ExecutionContext context, // Change the ExecutionContext namespace
    ILogger log)

    {
        //_logger.LogInformation("C# HTTP trigger function processed a request.");


        try
        {


            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            var model = JsonConvert.DeserializeObject<AccountIdModel>(requestBody);

            // Your custom logic here to get accounts
            var appDirectory = Directory.GetCurrentDirectory();
            var path = Path.Combine(context.FunctionAppDirectory, "appsettings.json");//{retrieves the directory path of the Azure Function App using Directory.GetCurrentDirectory()
                                                                                      //Then, it constructs the path to the appsettings.json}

            IConfigurationRoot config = new ConfigurationBuilder()//{ConfigurationBuilder is used to build a configuration object (IConfigurationRoot) by adding the appsettings.json file to it.
                                                                  //This allows the application to access configuration settings defined in the JSON file.}
                .SetBasePath(appDirectory)
                .AddJsonFile(path)
                .Build();

            var result = await _accountServices.SyncAccountById(model.Id, config);
            // var result = await _accountServices.GetAccounts(context);

            // Replace the response message with your actual response
            var responseMessage = JsonConvert.SerializeObject(result);

            return new OkObjectResult(result);


        }
        catch (AppException ex)
        {
            log.LogError(ex, "An application exception occurred."); // Log the exception
            return new ObjectResult(ex.AppExceptionErrorModel)
            {
                StatusCode = ex.StatusCode == int.MinValue ? 400 : ex.StatusCode  // or any other appropriate status code based on the exception
            };
        }
        catch (Exception ex)
        {
            log.LogError(ex, "An unexpected exception occurred."); // Log the exception
            return new StatusCodeResult(500); // 500 Internal Server Error
        }
    }
}
