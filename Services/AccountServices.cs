using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SyncingTenantUsers.Helpers;
using SyncingTenantUsers.Models.Contacts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using SyncingTenantUsers.Models.Logs;
using System.Xml.Linq;
using SyncingTenantUsers.Models.AccountLicenses;


namespace SyncingTenantUsers.Services
{
    public class AccountServices : IAccountServices
    {
        private readonly IConfiguration _configuration;

        public AccountServices(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IActionResult> GetAccounts()
        {
            try
            {
                var appDirectory = Directory.GetCurrentDirectory();
                var path = "C:\\Users\\Local_Admin\\source\\repos\\SyncingTenantUsers\\appsettings.json";

                IConfigurationRoot config = new ConfigurationBuilder()
                    .AddJsonFile(path)
                    .Build();

                string clientId = config["Authentication:ClientId"]!;
                string clientSecret = config["Authentication:ClientSecret"]!;
                string authority = config["Authentication:Authority"]!;
                string resource = config["Authentication:Resource"]!;
                string apiUrl = config["Authentication:ApiUrl"]!;

                DataverseAuthentication dataverseAuth = new DataverseAuthentication(clientId, clientSecret, authority, resource);
                string dataverseAccessToken = await dataverseAuth.GetAccessToken();

                using HttpClient httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dataverseAccessToken);
                //InputLogDetailModel logModel = new InputLogDetailModel
                //{
                //    psa_title = "Logging to Microsoft Graph",
                //    psa_additionalinformation = "Login succeeded to Microsoft Graph. Status code: OK.",
                //    psa_status = "Success",
                //    psa_statuscode = "OK",
                //    psa_Log_odata_bind = "/psa_logses(b3c0b8dd-c903-ef11-9f8a-000d3a68ce06)",
                //    psa_errormessage = null
                //};

                //var createLogJsonString = JsonConvert.SerializeObject(logModel);
                //HttpContent createLogContent = new StringContent(createLogJsonString, Encoding.UTF8, "application/json");
                //// var createLogJsonString = JsonConvert.SerializeObject(logModel);
                //HttpRequestMessage createLogRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}psa_logses");
                //createLogRequest.Headers.Add("Prefer", "return=representation");
                //createLogRequest.Content = createLogContent;
                //HttpResponseMessage createLogResponse = await httpClient.SendAsync(createLogRequest);


                //if (!createLogResponse.IsSuccessStatusCode)
                //{
                //    var errorMessage = await createLogResponse.Content.ReadAsStringAsync();
                //    throw new AppException($"Failed to create log. Status code: {createLogResponse.StatusCode}. Error message: {errorMessage}", "InsertLog", _configuration);
                //}

                HttpResponseMessage accountResponse = await httpClient.GetAsync($"{apiUrl}accounts?$filter=psa_tenantid ne null");

                if (accountResponse.IsSuccessStatusCode || ((int)accountResponse.StatusCode >= 200 && (int)accountResponse.StatusCode <= 209))
                {
                    string accountJsonResponse = await accountResponse.Content.ReadAsStringAsync();
                    dynamic accountJsonObject = JsonConvert.DeserializeObject(accountJsonResponse);

                    if (accountJsonObject != null && accountJsonObject.value != null)
                    {
                        foreach (var accountRecord in accountJsonObject.value)
                        {
                            string tenantId = accountRecord["psa_tenantid"].ToString();
                            string accountId = accountRecord["accountid"].ToString();
                            string accountClientId = accountRecord["psa_clientid"].ToString();
                            string accountClientSecret = accountRecord["psa_clientsecret"].ToString();
                            string accountName = accountRecord["name"].ToString();

                            // Construct the Azure AD token endpoint URL
                            string tokenEndpointUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

                            // Acquire access token for the tenant
                            string LoginAccessToken = await AcquireAccessToken(accountClientId, accountClientSecret, tokenEndpointUrl);

                            if (LoginAccessToken != null)
                            {
                                List<AccountLicensesModel> customerLicensesList = new List<AccountLicensesModel>();
                                // Send the GET request to get the subscribed SKUsFor that Tenant
                                string apiUrlAccountLicenses = $"https://graph.microsoft.com/v1.0/subscribedSkus?$select=accountId,skuId,skuPartNumber,consumedUnits,enabled&$filter=accountId eq '{tenantId}'";
                                HttpClient httpClientAcocuntLicenses = new HttpClient();
                                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);

                                HttpResponseMessage AccountLicenseResponse = await httpClient.GetAsync(apiUrlAccountLicenses);

                                if (AccountLicenseResponse.IsSuccessStatusCode)
                                {

                                    
                                    string CustomerLicensejsonResponse = await AccountLicenseResponse.Content.ReadAsStringAsync();
                                    dynamic CustomerLicenseResult = JsonConvert.DeserializeObject(CustomerLicensejsonResponse);

                                    // Extract the necessary fields and create AccountLicensesModel objects
                                    foreach (var customerLicense in CustomerLicenseResult.value)
                                    {
                                        AccountLicensesModel customerLicenseModel = new AccountLicensesModel
                                        {
                                            psa_accountname_account_odata_bind = $"/accounts({accountId})",
                                            psa_licenseid = customerLicense.skuId,
                                            psa_quantityassigned = customerLicense.consumedUnits.ToString(),
                                            psa_quantitypurchased = customerLicense.prepaidUnits.enabled.ToString(),
                                            psa_lastlicenserefresh = DateTime.UtcNow.ToString(),
                                           // psa_startdate = DateTime.UtcNow.ToString(),
                                            //psa_enddate = DateTime.UtcNow.ToString(),
                                        };
                                        customerLicensesList.Add(customerLicenseModel);
                                    }
                                }
                                // Get access token for the accountLicenses table
                                string accountLicenseAccessToken = await dataverseAuth.GetAccessToken();

                                // Create HttpClient for account licenses
                                using HttpClient httpAccountLicenseClient = new HttpClient();

                                // Construct URL for querying account licenses
                                string accountLicenseUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountId}";

                                // Set authorization header
                                httpAccountLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountLicenseAccessToken);

                                // Get account licenses from Dataverse
                                HttpResponseMessage accountLicenseResponse = await httpAccountLicenseClient.GetAsync(accountLicenseUrl);

                                // Check if the request was successful
                                if (accountLicenseResponse.IsSuccessStatusCode || ((int)accountLicenseResponse.StatusCode >= 200 && (int)accountLicenseResponse.StatusCode <= 209))
                                {
                                    // Deserialize the response JSON
                                    dynamic accountLicensesJsonObject = JsonConvert.DeserializeObject(await accountLicenseResponse.Content.ReadAsStringAsync());

                                    // Extract the list of account licenses
                                    List<AccountLicensesModel> accountLicenses = accountLicensesJsonObject.GetValue("value").ToObject<List<AccountLicensesModel>>();

                                    // Loop through each user
                                    foreach (var customerLicense in customerLicensesList)
                                    {
                                        // Serialize the user object to JSON
                                        string jsonCustomerLicense = JsonConvert.SerializeObject(customerLicense);
                                        HttpContent createAccountLicenseContent = new StringContent(jsonCustomerLicense, Encoding.UTF8, "application/json");

                                        // Check if the customerLicense is already in the accountlicenses list
                                        if (accountLicenses.Find(u => u.psa_licenseid == customerLicense.psa_licenseid) == null)
                                        {
                                            // Perform an insert operation
                                            accountLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_accountLicenses", createAccountLicenseContent);
                                        }
                                        else
                                        {
                                            // Find the existing account license
                                            var accountLicense = accountLicenses.Find(u => u.psa_licenseid == customerLicense.psa_licenseid);

                                            // Perform an update operation
                                            accountLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_accountLicenses({accountLicense.psa_licenseid})", createAccountLicenseContent);
                                        }

                                        // Read the response body
                                        string responseBody = await accountLicenseResponse.Content.ReadAsStringAsync();
                                        Console.WriteLine(responseBody);
                                    }
                                }


                                // Get List users for the current tenant from the Microsoft Graph API
                                List<InputContactModel> users = await GetUsersFromGraphApi(httpClient, LoginAccessToken, accountId);

                                // Get contacts for the current tenant from the Dataverse Contact table
                                string contactAccessToken = await dataverseAuth.GetAccessToken();
                                using HttpClient httpContactClient = new HttpClient();
                                string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountId}";
                                
                                httpContactClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactAccessToken);
                                HttpResponseMessage contact365Response = await httpContactClient.GetAsync(contactUrl);

                                if (contact365Response.IsSuccessStatusCode || ((int)contact365Response.StatusCode >= 200 && (int)contact365Response.StatusCode <= 209))
                                {
                                    dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                                    List<ContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<ContactModel>>();//table

                                    foreach (var user in users)
                                    {

                                        string jsonUser = JsonConvert.SerializeObject(user);
                                        HttpContent createContactContent = new StringContent(jsonUser, Encoding.UTF8, "application/json");

                                        // Check if the user's email is already in the contacts list
                                        if (contacts.Find(u => u.emailaddress1 == user.emailaddress1) == null)
                                        {
                                            // Perform an insert operation
                                            contact365Response = await httpContactClient.PostAsync($"{apiUrl}contacts", createContactContent);
                                        }
                                        else
                                        {
                                            var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                                            // Perform an update operation
                                            contact365Response = await httpContactClient.PatchAsync($"{apiUrl}contacts({contact.contactid})", createContactContent);

                                        }

                                        string responseBody = await contact365Response.Content.ReadAsStringAsync();
                                        Console.WriteLine(responseBody);

                                        // Check if the contact operation failed
                                        if (!contact365Response.IsSuccessStatusCode && !((int)contact365Response.StatusCode >= 200 && (int)contact365Response.StatusCode <= 209))
                                        {
                                            // Handle the error
                                            string errorMessage = await contact365Response.Content.ReadAsStringAsync();
                                            Console.WriteLine($"Error: {errorMessage}");
                                            return new ObjectResult($"Error: {errorMessage}") { StatusCode = StatusCodes.Status500InternalServerError };
                                        }
                                    }
                                }
                                else
                                {
                                    // Handle token acquisition failure
                                    return new ObjectResult("Failed to retrieve contacts from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                }
                            }
                            else
                            {
                                // Handle token acquisition failure
                                return new ObjectResult("Failed to acquire access token for the tenant") { StatusCode = StatusCodes.Status500InternalServerError };
                            }
                        }

                        return new OkObjectResult("Accounts processed successfully");
                    }
                    else
                    {
                        return new ObjectResult("Account JSON object is null or empty") { StatusCode = StatusCodes.Status400BadRequest };
                    }
                }
                else
                {
                    return new ObjectResult("Failed to retrieve accounts") { StatusCode = (int)accountResponse.StatusCode };
                }
            }
            catch (Exception ex)
            {
                // Log or handle any exceptions
                return new ObjectResult($"An error occurred: {ex.Message}") { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }


        public async Task<string> AcquireAccessToken(string clientId, string clientSecret, string tokenEndpointUrl)
        {
            try
            {
                using HttpClient httpClient = new();

                var tokenRequestBody = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "resource", "https://graph.microsoft.com" }
                });

                HttpResponseMessage tokenResponse = await httpClient.PostAsync(tokenEndpointUrl, tokenRequestBody);

                if (tokenResponse.IsSuccessStatusCode)
                {
                    var tokenResponseData = await tokenResponse.Content.ReadAsStringAsync();
                    dynamic tokenData = JsonConvert.DeserializeObject(tokenResponseData);
                    return tokenData.access_token;
                }
                else
                {
                    // Log or handle token acquisition failure
                    return null;
                }
            }
            catch (Exception ex)
            {
                // Log or handle any exceptions
                return ex.Message.ToString();

            }
        }

        public async Task<List<InputContactModel>> GetUsersFromGraphApi(HttpClient httpClient, string accessToken, string accountId)
        {
            try
            {
                string graphApiUrl = $"https://graph.microsoft.com/v1.0/users?$filter=(mail ne null)&$top=999&$count=true";

                // Set the Authorization header with the access token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, graphApiUrl);
                request.Headers.Add("ConsistencyLevel", "eventual");
                HttpResponseMessage usersResponse = await httpClient.SendAsync(request);
               

                if (usersResponse.IsSuccessStatusCode || ((int)usersResponse.StatusCode >= 200 && (int)usersResponse.StatusCode <= 209))
                {
                    string usersResponseBody = await usersResponse.Content.ReadAsStringAsync();
                    dynamic usersJsonObject = JsonConvert.DeserializeObject(usersResponseBody);


                    List<InputContactModel> users = new List<InputContactModel>();

                    foreach (var userRecord in usersJsonObject.value)
                    {
                        if (userRecord != null)
                        {
                            if (userRecord["userPrincipalName"].ToString().Contains("#EXT#"))
                            {
                                continue;
                            }
                            string displayName = userRecord["displayName"];
                            string[] nameParts = displayName.Split(' ');

                            string userFirstName = nameParts[0];
                            string userLastName = string.Join(' ', nameParts.Skip(1));

                            var user = new InputContactModel
                            {
                                parentcustomerid_account_odata_bind = $"/accounts({accountId})",
                                yomifullname = userRecord.displayName,
                                firstname = userFirstName,
                                lastname = userLastName,
                                emailaddress1 = userRecord.mail,
                                adx_identity_username = userRecord.userPrincipalName
                            };
                            users.Add(user);
                            Console.WriteLine(user);

                        }
                    }
                    //return list of contacts
                    Console.WriteLine(users.Count);
                    return users;

                }
                else
                {
                    // Log or handle user retrieval failure
                    return null;
                }
            }
            catch (Exception )
            {
                // Log or handle any exceptions

                return new List<InputContactModel>();
            }
        }
    }
}


