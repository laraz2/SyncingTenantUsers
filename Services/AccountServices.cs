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
using SyncingTenantUsers.Models.AccountLicenses;
using SyncingTenantUsers.Models.ContactLicenses;
using Newtonsoft.Json.Linq;
using SyncingTenantUsers.Models.User_Licenses;
using SyncingTenantUsers.Models.M365_Products;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs;
using SyncingTenantUsers.Models.Accounts;
using System.Net;


[assembly: FunctionsStartup(typeof(SyncingTenantUsers.Startup))]

namespace SyncingTenantUsers.Services
{
    public class AccountServices : IAccountServices
    {
        private readonly IConfiguration _configuration;

        public AccountServices(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<IActionResult> GetAccounts(IConfiguration config)
        {
            try
            {


                string clientId = config["Authentication:ClientId"]!;
                string clientSecret = config["Authentication:ClientSecret"]!;
                string authority = config["Authentication:Authority"]!;
                string resource = config["Authentication:Resource"]!;
                string apiUrl = config["Authentication:ApiUrl"]!;

                DataverseAuthentication dataverseAuth = new DataverseAuthentication(clientId, clientSecret, authority, resource);
                string accessToken = await dataverseAuth.GetAccessToken();

                HttpClient httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                //get accounts from dataverse
                HttpResponseMessage accountResponse = await httpClient.GetAsync($"{apiUrl}accounts?$filter=psa_tenantid ne null");
                if (!accountResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await accountResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch dataverse accounts failed", errorMessageText, accountResponse.StatusCode);
                }
                string accountJsonResponse = await accountResponse.Content.ReadAsStringAsync();
                dynamic accountJsonObject = JsonConvert.DeserializeObject(accountJsonResponse);
                //get M365 Product 
                HttpResponseMessage M365ProductsResponse = await httpClient.GetAsync($"{apiUrl}psa_m365productses");
                if (!M365ProductsResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await M365ProductsResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch dataverse M365Licenses failed", errorMessageText, M365ProductsResponse.StatusCode);
                }


                string m365ProductResponse = await M365ProductsResponse.Content.ReadAsStringAsync();
                dynamic m365ProductsJsonObject = JsonConvert.DeserializeObject(m365ProductResponse);
                // Deserialize the JSON array into a list of M365ProductsModel
                List<M365ProductsModel> m365ProductList = JsonConvert.DeserializeObject<List<M365ProductsModel>>(m365ProductsJsonObject.value.ToString());

                if (accountJsonObject != null && accountJsonObject.value != null)
                {

                    //for each account :
                    foreach (var accountRecord in accountJsonObject.value)
                    {
                        string tenantId = accountRecord["psa_tenantid"].ToString();
                        string accountId = accountRecord["accountid"].ToString();
                        string accountClientId = accountRecord["psa_clientid"].ToString();
                        string accountClientSecret = accountRecord["psa_clientsecret"].ToString();
                        string accountName = accountRecord["name"].ToString();

                        // Construct the Azure AD token endpoint URL
                        string tokenEndpointUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

                        // Acquire access token for the tenant in micros
                        string LoginAccessToken = await AcquireAccessToken(accountClientId, accountClientSecret, tokenEndpointUrl);

                        if (LoginAccessToken == null)
                        {
                            continue;
                        }
                        // Get access token for the accountLicenses table
                        accessToken = await dataverseAuth.GetAccessToken();

                        // Create HttpClient for account licenses
                        httpClient = new HttpClient();

                        // get account licenses for that account 
                        string accountLicenseUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountId}";

                        // Set authorization header
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                        // Get account licenses from Dataverse
                        HttpResponseMessage accountLicenseResponse = await httpClient.GetAsync(accountLicenseUrl);
                        if (!accountLicenseResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await accountLicenseResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            continue;
                        }

                        // Deserialize the accountLicenses response JSON
                        dynamic accountLicensesJsonObject = JsonConvert.DeserializeObject(await accountLicenseResponse.Content.ReadAsStringAsync());
                        // Extract the list of account licenses from dataverse and put it in account licenses list
                        List<AccountLicensesOutputModel> accountLicenses = accountLicensesJsonObject.GetValue("value").ToObject<List<AccountLicensesOutputModel>>();

                        //getting the Customer Licenses for the current account frpm microsoft api
                        List<CustomerLicensesModel> customerLicenses = new List<CustomerLicensesModel>();

                        // Send the GET request to get the subscribed SKUsFor that Tenant , customerLicenses
                        string apiUrlCustomerLicenses = $"https://graph.microsoft.com/v1.0/subscribedSkus?$select=skuPartNumber,skuId,consumedUnits,prepaidUnits&$filter accountId eq '{tenantId}'";
                        HttpClient httpClientCustomerLicenses = new HttpClient();
                        httpClientCustomerLicenses.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                        httpClientCustomerLicenses.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");

                        HttpResponseMessage CustomerLicenseResponse = await httpClientCustomerLicenses.GetAsync(apiUrlCustomerLicenses);

                        if (!CustomerLicenseResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await CustomerLicenseResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            continue;
                        }
                        string CustomerLicensejsonResponse = await CustomerLicenseResponse.Content.ReadAsStringAsync();
                        dynamic CustomerLicenseResult = JsonConvert.DeserializeObject(CustomerLicensejsonResponse);

                        // Extract the necessary fields and create AccountLicensesModel objects
                        foreach (var customerLicense in CustomerLicenseResult.value)
                        {
                            var matchingProduct = m365ProductList.FirstOrDefault(product => product.psa_guid.ToString() == customerLicense.skuId.Value.ToString());
                            if (matchingProduct == null)
                            {
                                continue;
                            }

                            var m365ProductId = matchingProduct.psa_m365productsid;
                            CustomerLicensesModel customerLicenseModel = new CustomerLicensesModel
                            {
                                psa_accountName_odata_bind = $"/accounts({accountId})",
                                psa_accountlicensenumber = accountName + " - " + customerLicense.skuPartNumber.ToString(),
                                psa_licenseid = customerLicense.skuId, //guid
                                psa_quantityassigned = customerLicense.consumedUnits,
                                psa_quantitypurchased = customerLicense.prepaidUnits.enabled,
                                psa_lastlicenserefresh = DateTime.UtcNow.ToString(),
                                //psa_startdate = DateTime.UtcNow.ToString(),
                                //psa_enddate = DateTime.UtcNow.ToString(),
                                psa_ProductStringId_odata_bind = $"/psa_m365productses({m365ProductId})"
                            };
                            string jsonCustomerLicense = JsonConvert.SerializeObject(customerLicenseModel);
                            HttpContent createAccountLicenseContent = new StringContent(jsonCustomerLicense, Encoding.UTF8, "application/json");//input model

                            customerLicenses.Add(customerLicenseModel);
                            var licenseId = customerLicense.psa_licenseid;
                            // Check if the customerLicense is already in the accountlicenses table
                            if (accountLicenses.Find(u => u.psa_licenseid.ToString() == customerLicense.skuId.ToString()) == null)
                            {
                                // Perform an insert operation
                                HttpResponseMessage accountLicensePostResponse = await httpClient.PostAsync($"{apiUrl}psa_accountlicenseses", createAccountLicenseContent);
                                if (!accountLicensePostResponse.IsSuccessStatusCode)
                                {
                                    var errorMessage = await accountLicensePostResponse.Content.ReadAsStringAsync();
                                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                    // Extract the error message from the JSON object
                                    string errorMessageText = errorJson.error.message;
                                    ////////////////////// log
                                }

                            }
                            else
                            {
                                // Find the existing account license
                                AccountLicensesOutputModel accountLicense = accountLicenses.Find(u => u.psa_licenseid.ToString() == customerLicense.skuId.ToString());
                                string accountLicenseId = accountLicense.psa_accountlicensesid;
                                // Perform an update operation
                                HttpResponseMessage accountLicenseUpdateResponse = await httpClient.PatchAsync($"{apiUrl}psa_accountlicenseses({accountLicenseId})", createAccountLicenseContent);
                                if (!accountLicenseUpdateResponse.IsSuccessStatusCode)
                                {
                                    var errorMessage = await accountLicenseUpdateResponse.Content.ReadAsStringAsync();
                                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                    // Extract the error message from the JSON object
                                    string errorMessageText = errorJson.error.message;
                                    ////////////////log 
                                }


                            }



                        }



                        //get contacts from dataverse
                        accessToken = await dataverseAuth.GetAccessToken();
                        string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountId}";

                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                        HttpResponseMessage contact365Response = await httpClient.GetAsync(contactUrl);
                        //string contactResponseBody = await contact365Response.Content.ReadAsStringAsync();
                        //Console.WriteLine(contactResponseBody);
                        if (!contact365Response.IsSuccessStatusCode)
                        {
                            var errorMessage = await contact365Response.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            continue;
                        }

                        dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                        List<OutputContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<OutputContactModel>>();//table
                        var contactId = "";

                        List<User_LicensesModel> user_Licenses = new List<User_LicensesModel>();
                        // List<UsersModel> users = new List<UsersModel>();
                        List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                        // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                        string apiUrlUser_Licenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null and assignedLicenses/$count ne 0&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                        HttpClient httpClientUserLicenses = new HttpClient();
                        httpClientUserLicenses.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                        httpClientUserLicenses.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
                        HttpResponseMessage User_LicenseResponse = await httpClientUserLicenses.GetAsync(apiUrlUser_Licenses);



                        if (!User_LicenseResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await User_LicenseResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            continue;
                        }
                        string User_LicensejsonResponse = await User_LicenseResponse.Content.ReadAsStringAsync();

                        dynamic User_LicenseResult = JsonConvert.DeserializeObject(User_LicensejsonResponse);

                        // Extract the necessary fields and create User_LicensesModel objects,loop through user_License List
                        foreach (var user_License in User_LicenseResult.value)
                        {
                            // Check if the userPrincipalName contains "#EXT#", if it does, skip
                            if (user_License["userPrincipalName"].ToString().Contains("#EXT#"))
                            {
                                continue;
                            }
                            // Check if assignedLicenses is not null and contains any elements
                            //if (user_License["assignedLicenses"] == null || user_License["assignedLicenses"].Count == 0)
                            //{
                            //    // Skip processing users with no assigned licenses
                            //    continue;
                            //}
                            string displayName = user_License["displayName"];
                            string[] nameParts = displayName.Split(' ');

                            string userFirstName = nameParts[0];
                            string userLastName = string.Join(' ', nameParts.Skip(1));

                            UsersModel user = new UsersModel
                            {
                                parentcustomerid_account_odata_bind = $"/accounts({accountId})",
                                // yomifullname = user_License["displayName"],
                                firstname = userFirstName,
                                lastname = userLastName,
                                emailaddress1 = user_License["mail"],
                                adx_identity_username = user_License["displayName"],
                                psa_lastsynceddate = DateTime.UtcNow.ToString()
                            };
                            // Add the license model to the list
                            //users.Add(user);
                            string jsonUser = JsonConvert.SerializeObject(user);
                            HttpContent createContactContent = new StringContent(jsonUser, Encoding.UTF8, "application/json");

                            // Check if the user's email is already in the contacts list

                            if (contacts.Find(u => u.emailaddress1 == user.emailaddress1) == null)
                            {
                                // Perform an insert operation
                                //contact365Response = await httpContactClient.PostAsync($"{apiUrl}contacts", createContactContent);
                                HttpRequestMessage createContactRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}contacts");
                                createContactRequest.Headers.Add("Prefer", "return=representation");
                                createContactRequest.Headers.Add("ConsistencyLevel", "eventual"); // Adding ConsistencyLevel header
                                createContactRequest.Content = createContactContent;

                                HttpResponseMessage createContactResponse = await httpClient.SendAsync(createContactRequest);
                                if (!createContactResponse.IsSuccessStatusCode)
                                {
                                    var errorMessage = await createContactResponse.Content.ReadAsStringAsync();
                                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                    // Extract the error message from the JSON object
                                    string errorMessageText = errorJson.error.message;
                                    continue;
                                }
                                var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                                var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                                OutputContactModel outputContactModel = logContactJson.ToObject<OutputContactModel>();

                                contactId = outputContactModel.contactid;
                                foreach (var license in user_License.assignedLicenses)
                                {
                                    //var nbr = user_License.assignedLicenses.Count;
                                    //Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                                    var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                                    if (m365product == null)
                                    {
                                        continue;
                                    }

                                    accessToken = await dataverseAuth.GetAccessToken();
                                    string accountLicensesUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountId} and psa_licenseid eq {license.skuId}";

                                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                                    HttpResponseMessage accountLicenseIdResponse = await httpClient.GetAsync(accountLicensesUrl);


                                    string accountlicensesIdjson = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                                    //Console.WriteLine(accountlicensesIdjson);
                                    if (!accountLicenseIdResponse.IsSuccessStatusCode)
                                    {
                                        var errorMessage = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                                        dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                        // Extract the error message from the JSON object
                                        string errorMessageText = errorJson.error.message;
                                        continue;
                                    }
                                    var accountlicensesIdObject = JsonConvert.DeserializeObject<JObject>(accountlicensesIdjson);

                                    if (accountlicensesIdObject == null || accountlicensesIdObject["value"] == null)
                                    {
                                        //accountlicense not found
                                        continue;
                                    }

                                    List<AccountLicensesOutputModel> accountlicenses = accountlicensesIdObject["value"].ToObject<List<AccountLicensesOutputModel>>();
                                    if (accountlicenses.Count == 0)
                                    {
                                        //accountlicense not found
                                        continue;
                                    }
                                    var accountlicense = accountlicenses.FirstOrDefault();

                                    var m365productId = m365product.psa_m365productsid;
                                    var accountlicenseid = accountlicense.psa_accountlicensesid;

                                    // Create UserLicensesModel object for each assigned license
                                    UserLicensesModel userLicenseModel = new UserLicensesModel
                                    {
                                        psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                        psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                        psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                                    };


                                    // Serialize the userLicense object to JSON
                                    string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                                    HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model


                                    // Perform an insert operation
                                    HttpResponseMessage contactLicenseCreateResponse = await httpClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);

                                    if (!contactLicenseCreateResponse.IsSuccessStatusCode)
                                    {

                                        // write error to log file
                                        var errorMessage = await M365ProductsResponse.Content.ReadAsStringAsync();
                                        dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                        // Extract the error message from the JSON object
                                        string errorMessageText = errorJson.error.message;
                                        continue;
                                    }



                                }


                            }

                            else
                            {
                                var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                                contactId = contact.contactid;
                                // Perform an update operation
                                HttpResponseMessage contact365UpdateResponse = await httpClient.PatchAsync($"{apiUrl}contacts({contactId})", createContactContent);
                                //string r = await contact365Response.Content.ReadAsStringAsync();
                                //Console.WriteLine(r);
                                if (!contact365UpdateResponse.IsSuccessStatusCode)
                                {
                                    var errorMessage = await contact365UpdateResponse.Content.ReadAsStringAsync();
                                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                    // Extract the error message from the JSON object
                                    string errorMessageText = errorJson.error.message;
                                    continue;
                                }

                                foreach (var license in user_License.assignedLicenses)
                                {
                                    //var nbr = user_License.assignedLicenses.Count;
                                    //Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                                    var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                                    if (m365product == null)
                                    {
                                        continue;
                                    }
                                    accessToken = await dataverseAuth.GetAccessToken();
                                    string accountLicensesUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountId}";

                                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                                    HttpResponseMessage accountLicenseIdResponse = await httpClient.GetAsync(accountLicensesUrl);


                                    string accountlicensesIdjson = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                                    //Console.WriteLine(accountlicensesIdjson);
                                    if (!accountLicenseIdResponse.IsSuccessStatusCode)
                                    {
                                        var errorMessage = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                                        dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                        // Extract the error message from the JSON object
                                        string errorMessageText = errorJson.error.message;
                                        continue;
                                    }
                                    var accountlicensesIdObject = JsonConvert.DeserializeObject<JObject>(accountlicensesIdjson);

                                    if (accountlicensesIdObject == null || accountlicensesIdObject["value"] == null)
                                    {
                                        //accountlicense not found
                                        continue;
                                    }

                                    List<AccountLicensesOutputModel> accountlicenses = accountlicensesIdObject["value"].ToObject<List<AccountLicensesOutputModel>>();
                                    if (accountlicenses.Count == 0)
                                    {
                                        //accountlicense not found
                                        continue;
                                    }
                                    var accountlicense = accountlicenses.FirstOrDefault();

                                    var m365productId = m365product.psa_m365productsid;
                                    var accountlicenseid = accountlicense.psa_accountlicensesid;

                                    // Create UserLicensesModel object for each assigned license
                                    UserLicensesModel userLicenseModel = new UserLicensesModel
                                    {
                                        psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                        psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                        psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                                    };
                                    string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                                    HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model


                                    string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses?$filter= _psa_productstringid_value eq {m365productId} and _psa_contactprincipalname_value eq {contactId} and _psa_accountlicenseid_value eq {accountlicenseid}";
                                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                                    HttpResponseMessage contactLicenseResponse = await httpClient.GetAsync(contactLicenseUrl);

                                    if (!contactLicenseResponse.IsSuccessStatusCode)
                                    {
                                        var errorMessage = await contactLicenseResponse.Content.ReadAsStringAsync();
                                        dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                        // Extract the error message from the JSON object
                                        string errorMessageText = errorJson.error.message;
                                        continue;
                                    }

                                    // Deserialize the contact license response JSON
                                    dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                    // Extract the list of contact licenses

                                    if (contactLicensesJsonObject == null || contactLicensesJsonObject["value"] == null)
                                    {
                                        //accountlicense not found
                                        continue;
                                    }
                                    List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                    if (contactLicenses.Count == 0)
                                    {
                                        //contactlicense not found

                                        HttpResponseMessage contactLicenseCreateResponse = await httpClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);

                                        if (!contactLicenseCreateResponse.IsSuccessStatusCode)
                                        {
                                            var errorMessage = await contactLicenseCreateResponse.Content.ReadAsStringAsync();
                                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                            // Extract the error message from the JSON object
                                            string errorMessageText = errorJson.error.message;
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        var contactLicense = contactLicenses.FirstOrDefault();
                                        HttpResponseMessage contactLicenseUpdateResponse = await httpClient.PatchAsync($"{apiUrl}psa_contactlicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);

                                        if (!contactLicenseUpdateResponse.IsSuccessStatusCode)
                                        {
                                            var errorMessage = await contactLicenseUpdateResponse.Content.ReadAsStringAsync();
                                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                            // Extract the error message from the JSON object
                                            string errorMessageText = errorJson.error.message;
                                            continue;
                                        }

                                    }

                                }

                            }

                        }


                        httpClient.Dispose();
                    }

                }
                else
                {
                    return new ObjectResult("Account JSON object is null or empty") { StatusCode = StatusCodes.Status400BadRequest };
                }


                return new OkObjectResult("Accounts Processed Successfully!");
            }

            catch (AppException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new AppException("A system error has occurred", e.InnerException != null ? e.InnerException.Message : e.Message);
            }
        }

        public async Task<IActionResult> SyncAccountById(string accountGuid,IConfiguration config)
        {
            try
            {
             

                string clientId = config["Authentication:ClientId"]!;
                string clientSecret = config["Authentication:ClientSecret"]!;
                string authority = config["Authentication:Authority"]!;
                string resource = config["Authentication:Resource"]!;
                string apiUrl = config["Authentication:ApiUrl"]!;

                DataverseAuthentication dataverseAuth = new DataverseAuthentication(clientId, clientSecret, authority, resource);
                string accessToken = await dataverseAuth.GetAccessToken();

                HttpClient httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                //get accounts from dataverse
                HttpResponseMessage accountResponse = await httpClient.GetAsync($"{apiUrl}accounts({accountGuid})");
                if (!accountResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await accountResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch dataverse accounts failed", errorMessageText, accountResponse.StatusCode);
                }
                string accountJsonResponse = await accountResponse.Content.ReadAsStringAsync();
                dynamic accountJsonObject = JsonConvert.DeserializeObject(accountJsonResponse);
                GetAccountModel accountRecord = accountJsonObject.ToObject<GetAccountModel>();

                //get M365 Product 
                HttpResponseMessage M365ProductsResponse = await httpClient.GetAsync($"{apiUrl}psa_m365productses");
                if (!M365ProductsResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await M365ProductsResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch dataverse M365Licenses failed", errorMessageText, M365ProductsResponse.StatusCode);
                }


                string m365ProductResponse = await M365ProductsResponse.Content.ReadAsStringAsync();
                dynamic m365ProductsJsonObject = JsonConvert.DeserializeObject(m365ProductResponse);

                // Deserialize the JSON array into a list of M365ProductsModel
                List<M365ProductsModel> m365ProductList = JsonConvert.DeserializeObject<List<M365ProductsModel>>(m365ProductsJsonObject.value.ToString());


                string tenantId = accountRecord.psa_tenantid;
                string accountClientId = accountRecord.psa_clientid;
                string accountClientSecret = accountRecord.psa_clientsecret;
                string accountName = accountRecord.name;
                if(string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(accountClientId) || string.IsNullOrEmpty(clientSecret))
                {
                    throw new AppException("Azure keys are invalid for this customer", HttpStatusCode.BadRequest);

                }
                // Construct the Azure AD token endpoint URL
                string tokenEndpointUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/token";

                // Acquire access token for the tenant in micros
                string LoginAccessToken = await AcquireAccessToken(accountClientId, accountClientSecret, tokenEndpointUrl);

                if (LoginAccessToken == null)
                {
                    throw new AppException("Failed to acquire Azure access token", HttpStatusCode.BadRequest);
                }
                // Get access token for the accountLicenses table
                accessToken = await dataverseAuth.GetAccessToken();

                // Create HttpClient for account licenses
                httpClient = new HttpClient();

                // get account licenses for that account 
                string accountLicenseUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountGuid}";

                // Set authorization header
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Get account licenses from Dataverse
                HttpResponseMessage accountLicenseResponse = await httpClient.GetAsync(accountLicenseUrl);
                if (!accountLicenseResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await accountLicenseResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch dataverse accountLicenses failed", errorMessageText, HttpStatusCode.BadRequest);
                }

                // Deserialize the response JSON
                dynamic accountLicensesJsonObject = JsonConvert.DeserializeObject(await accountLicenseResponse.Content.ReadAsStringAsync());
                // Extract the list of account licenses from dataverse and put it in account licenses list
                List<AccountLicensesOutputModel> accountLicenses = accountLicensesJsonObject.GetValue("value").ToObject<List<AccountLicensesOutputModel>>();

                //getting the Customer Licenses for the current account frpm microsoft api
                List<CustomerLicensesModel> customerLicenses = new List<CustomerLicensesModel>();

                // Send the GET request to get the subscribed SKUsFor that Tenant , customerLicenses
                string apiUrlCustomerLicenses = $"https://graph.microsoft.com/v1.0/subscribedSkus?$select=skuPartNumber,skuId,consumedUnits,prepaidUnits&$filter accountId eq '{tenantId}'";
                HttpClient httpClientCustomerLicenses = new HttpClient();
                httpClientCustomerLicenses.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                httpClientCustomerLicenses.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");

                HttpResponseMessage CustomerLicenseResponse = await httpClientCustomerLicenses.GetAsync(apiUrlCustomerLicenses);

                if (!CustomerLicenseResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await CustomerLicenseResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch subscribedSkus failed", errorMessageText, HttpStatusCode.BadRequest);
                    
                }
                string CustomerLicensejsonResponse = await CustomerLicenseResponse.Content.ReadAsStringAsync();
                dynamic CustomerLicenseResult = JsonConvert.DeserializeObject(CustomerLicensejsonResponse);

                // Extract the necessary fields and create AccountLicensesModel objects
                foreach (var customerLicense in CustomerLicenseResult.value)
                {
                    var matchingProduct = m365ProductList.FirstOrDefault(product => product.psa_guid.ToString() == customerLicense.skuId.Value.ToString());
                    if (matchingProduct == null)
                    {
                        continue;
                    }

                    var m365ProductId = matchingProduct.psa_m365productsid;
                    CustomerLicensesModel customerLicenseModel = new CustomerLicensesModel
                    {
                        psa_accountName_odata_bind = $"/accounts({accountGuid})",
                        psa_accountlicensenumber = accountName + " - " + customerLicense.skuPartNumber.ToString(),
                        psa_licenseid = customerLicense.skuId, //guid
                        psa_quantityassigned = customerLicense.consumedUnits,
                        psa_quantitypurchased = customerLicense.prepaidUnits.enabled,
                        psa_lastlicenserefresh = DateTime.UtcNow.ToString(),
                        //psa_startdate = DateTime.UtcNow.ToString(),
                        //psa_enddate = DateTime.UtcNow.ToString(),
                        psa_ProductStringId_odata_bind = $"/psa_m365productses({m365ProductId})"
                    };
                    string jsonCustomerLicense = JsonConvert.SerializeObject(customerLicenseModel);
                    HttpContent createAccountLicenseContent = new StringContent(jsonCustomerLicense, Encoding.UTF8, "application/json");//input model

                    customerLicenses.Add(customerLicenseModel);
                    var licenseId = customerLicense.psa_licenseid;
                    // Check if the customerLicense is already in the accountlicenses table
                    if (accountLicenses.Find(u => u.psa_licenseid.ToString() == customerLicense.skuId.ToString()) == null)
                    {
                        // Perform an insert operation
                        HttpResponseMessage accountLicensePostResponse = await httpClient.PostAsync($"{apiUrl}psa_accountlicenseses", createAccountLicenseContent);
                        if (!accountLicensePostResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await accountLicensePostResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            ////////////////////// log
                        }

                    }
                    else
                    {
                        // Find the existing account license
                        AccountLicensesOutputModel accountLicense = accountLicenses.Find(u => u.psa_licenseid.ToString() == customerLicense.skuId.ToString());
                        string accountLicenseId = accountLicense.psa_accountlicensesid;
                        // Perform an update operation
                        HttpResponseMessage accountLicenseUpdateResponse = await httpClient.PatchAsync($"{apiUrl}psa_accountlicenseses({accountLicenseId})", createAccountLicenseContent);
                        if (!accountLicenseUpdateResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await accountLicenseUpdateResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            ////////////////log 
                        }


                    }



                }



                //get contacts from dataverse
                accessToken = await dataverseAuth.GetAccessToken();
                string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountGuid}";

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                HttpResponseMessage contact365Response = await httpClient.GetAsync(contactUrl);
                //string contactResponseBody = await contact365Response.Content.ReadAsStringAsync();
                //Console.WriteLine(contactResponseBody);
                if (!contact365Response.IsSuccessStatusCode)
                {
                    var errorMessage = await contact365Response.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch dataverse contacts failed", errorMessageText, HttpStatusCode.BadRequest);
                }

                dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                List<OutputContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<OutputContactModel>>();//table
                var contactId = "";

                List<User_LicensesModel> user_Licenses = new List<User_LicensesModel>();
                // List<UsersModel> users = new List<UsersModel>();
                List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                string apiUrlUser_Licenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null and assignedLicenses/$count ne 0&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                HttpClient httpClientUserLicenses = new HttpClient();
                httpClientUserLicenses.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                httpClientUserLicenses.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
                HttpResponseMessage User_LicenseResponse = await httpClientUserLicenses.GetAsync(apiUrlUser_Licenses);



                if (!User_LicenseResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await User_LicenseResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    throw new AppException("Fetch users failed", errorMessageText, HttpStatusCode.BadRequest);
                }
                string User_LicensejsonResponse = await User_LicenseResponse.Content.ReadAsStringAsync();

                dynamic User_LicenseResult = JsonConvert.DeserializeObject(User_LicensejsonResponse);

                // Extract the necessary fields and create User_LicensesModel objects,loop through user_License List
                foreach (var user_License in User_LicenseResult.value)
                {
                    // Check if the userPrincipalName contains "#EXT#", if it does, skip
                    if (user_License["userPrincipalName"].ToString().Contains("#EXT#"))
                    {
                        continue;
                    }
                    // Check if assignedLicenses is not null and contains any elements
                    //if (user_License["assignedLicenses"] == null || user_License["assignedLicenses"].Count == 0)
                    //{
                    //    // Skip processing users with no assigned licenses
                    //    continue;
                    //}
                    string displayName = user_License["displayName"];
                    string[] nameParts = displayName.Split(' ');

                    string userFirstName = nameParts[0];
                    string userLastName = string.Join(' ', nameParts.Skip(1));

                    UsersModel user = new UsersModel
                    {
                        parentcustomerid_account_odata_bind = $"/accounts({accountGuid})",
                        // yomifullname = user_License["displayName"],
                        firstname = userFirstName,
                        lastname = userLastName,
                        emailaddress1 = user_License["mail"],
                        adx_identity_username = user_License["displayName"],
                        psa_lastsynceddate = DateTime.UtcNow.ToString()
                    };
                    // Add the license model to the list
                    //users.Add(user);
                    string jsonUser = JsonConvert.SerializeObject(user);
                    HttpContent createContactContent = new StringContent(jsonUser, Encoding.UTF8, "application/json");

                    // Check if the user's email is already in the contacts list

                    if (contacts.Find(u => u.emailaddress1 == user.emailaddress1) == null)
                    {
                        // Perform an insert operation
                        //contact365Response = await httpContactClient.PostAsync($"{apiUrl}contacts", createContactContent);
                        HttpRequestMessage createContactRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}contacts");
                        createContactRequest.Headers.Add("Prefer", "return=representation");
                        createContactRequest.Headers.Add("ConsistencyLevel", "eventual"); // Adding ConsistencyLevel header
                        createContactRequest.Content = createContactContent;

                        HttpResponseMessage createContactResponse = await httpClient.SendAsync(createContactRequest);
                        if (!createContactResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await createContactResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            continue;
                        }
                        var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                        var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                        OutputContactModel outputContactModel = logContactJson.ToObject<OutputContactModel>();

                        contactId = outputContactModel.contactid;
                        foreach (var license in user_License.assignedLicenses)
                        {
                            var nbr = user_License.assignedLicenses.Count;
                            Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                            var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                            if (m365product == null)
                            {
                                continue;
                            }

                            accessToken = await dataverseAuth.GetAccessToken();
                            string accountLicensesUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountGuid} and psa_licenseid eq {license.skuId}";

                            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                            HttpResponseMessage accountLicenseIdResponse = await httpClient.GetAsync(accountLicensesUrl);


                            string accountlicensesIdjson = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                            //Console.WriteLine(accountlicensesIdjson);
                            if (!accountLicenseIdResponse.IsSuccessStatusCode)
                            {
                                var errorMessage = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                                dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                // Extract the error message from the JSON object
                                string errorMessageText = errorJson.error.message;
                                continue;
                            }
                            var accountlicensesIdObject = JsonConvert.DeserializeObject<JObject>(accountlicensesIdjson);

                            if (accountlicensesIdObject == null || accountlicensesIdObject["value"] == null)
                            {
                                //accountlicense not found
                                continue;
                            }

                            List<AccountLicensesOutputModel> accountlicenses = accountlicensesIdObject["value"].ToObject<List<AccountLicensesOutputModel>>();
                            if (accountlicenses.Count == 0)
                            {
                                //accountlicense not found
                                continue;
                            }
                            var accountlicense = accountlicenses.FirstOrDefault();

                            var m365productId = m365product.psa_m365productsid;
                            var accountlicenseid = accountlicense.psa_accountlicensesid;

                            // Create UserLicensesModel object for each assigned license
                            UserLicensesModel userLicenseModel = new UserLicensesModel
                            {
                                psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                            };


                            // Serialize the userLicense object to JSON
                            string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                            HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model


                            // Perform an insert operation
                            HttpResponseMessage contactLicenseCreateResponse = await httpClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);

                            if (!contactLicenseCreateResponse.IsSuccessStatusCode)
                            {

                                // write error to log file
                                var errorMessage = await M365ProductsResponse.Content.ReadAsStringAsync();
                                dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                // Extract the error message from the JSON object
                                string errorMessageText = errorJson.error.message;
                                continue;
                            }



                        }


                    }

                    else
                    {
                        var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                        contactId = contact.contactid;
                        // Perform an update operation
                        HttpResponseMessage contact365UpdateResponse = await httpClient.PatchAsync($"{apiUrl}contacts({contactId})", createContactContent);
                        //string r = await contact365Response.Content.ReadAsStringAsync();
                        //Console.WriteLine(r);
                        if (!contact365UpdateResponse.IsSuccessStatusCode)
                        {
                            var errorMessage = await contact365UpdateResponse.Content.ReadAsStringAsync();
                            dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                            // Extract the error message from the JSON object
                            string errorMessageText = errorJson.error.message;
                            continue;
                        }

                        foreach (var license in user_License.assignedLicenses)
                        {
                            //var nbr = user_License.assignedLicenses.Count;
                            //Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                            var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                            if (m365product == null)
                            {
                                continue;
                            }
                            accessToken = await dataverseAuth.GetAccessToken();
                            string accountLicensesUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountGuid}";

                            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                            HttpResponseMessage accountLicenseIdResponse = await httpClient.GetAsync(accountLicensesUrl);


                            string accountlicensesIdjson = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                            //Console.WriteLine(accountlicensesIdjson);
                            if (!accountLicenseIdResponse.IsSuccessStatusCode)
                            {
                                var errorMessage = await accountLicenseIdResponse.Content.ReadAsStringAsync();
                                dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                // Extract the error message from the JSON object
                                string errorMessageText = errorJson.error.message;
                                continue;
                            }
                            var accountlicensesIdObject = JsonConvert.DeserializeObject<JObject>(accountlicensesIdjson);

                            if (accountlicensesIdObject == null || accountlicensesIdObject["value"] == null)
                            {
                                //accountlicense not found
                                continue;
                            }

                            List<AccountLicensesOutputModel> accountlicenses = accountlicensesIdObject["value"].ToObject<List<AccountLicensesOutputModel>>();
                            if (accountlicenses.Count == 0)
                            {
                                //accountlicense not found
                                continue;
                            }
                            var accountlicense = accountlicenses.FirstOrDefault();

                            var m365productId = m365product.psa_m365productsid;
                            var accountlicenseid = accountlicense.psa_accountlicensesid;

                            // Create UserLicensesModel object for each assigned license
                            UserLicensesModel userLicenseModel = new UserLicensesModel
                            {
                                psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                            };
                            string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                            HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model


                            string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses?$filter= _psa_productstringid_value eq {m365productId} and _psa_contactprincipalname_value eq {contactId} and _psa_accountlicenseid_value eq {accountlicenseid}";
                            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                            HttpResponseMessage contactLicenseResponse = await httpClient.GetAsync(contactLicenseUrl);

                            if (!contactLicenseResponse.IsSuccessStatusCode)
                            {
                                var errorMessage = await contactLicenseResponse.Content.ReadAsStringAsync();
                                dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                // Extract the error message from the JSON object
                                string errorMessageText = errorJson.error.message;
                                continue;
                            }

                            // Deserialize the contact license response JSON
                            dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                            // Extract the list of contact licenses

                            if (contactLicensesJsonObject == null || contactLicensesJsonObject["value"] == null)
                            {
                                //accountlicense not found
                                continue;
                            }
                            List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                            if (contactLicenses.Count == 0)
                            {
                                //contactlicense not found

                                HttpResponseMessage contactLicenseCreateResponse = await httpClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);

                                if (!contactLicenseCreateResponse.IsSuccessStatusCode)
                                {
                                    var errorMessage = await contactLicenseCreateResponse.Content.ReadAsStringAsync();
                                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                    // Extract the error message from the JSON object
                                    string errorMessageText = errorJson.error.message;
                                    continue;
                                }
                            }
                            else
                            {
                                var contactLicense = contactLicenses.FirstOrDefault();
                                HttpResponseMessage contactLicenseUpdateResponse = await httpClient.PatchAsync($"{apiUrl}psa_contactlicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);

                                if (!contactLicenseUpdateResponse.IsSuccessStatusCode)
                                {
                                    var errorMessage = await contactLicenseUpdateResponse.Content.ReadAsStringAsync();
                                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                                    // Extract the error message from the JSON object
                                    string errorMessageText = errorJson.error.message;
                                    continue;
                                }

                            }

                        }

                    }

                }
                return new OkObjectResult("Accounts Processed Successfully!");

            }


            catch (AppException )
            {
                throw;
            }
            catch (Exception e)
            {
                throw new AppException("A system error has occurred", e.InnerException != null ? e.InnerException.Message : e.Message);
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

                if (!tokenResponse.IsSuccessStatusCode)
                {
                    var errorMessage = await tokenResponse.Content.ReadAsStringAsync();
                    dynamic errorJson = JsonConvert.DeserializeObject(errorMessage);

                    // Extract the error message from the JSON object
                    string errorMessageText = errorJson.error.message;
                    return null;
                }
                else
                {
                    var tokenResponseData = await tokenResponse.Content.ReadAsStringAsync();
                    dynamic tokenData = JsonConvert.DeserializeObject(tokenResponseData);
                    return tokenData.access_token;
                    // Log or handle token acquisition failure
                }
            }
            catch (AppException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new AppException("A system error has occurred", e.InnerException != null ? e.InnerException.Message : e.Message);
            }
        }
    }
}
