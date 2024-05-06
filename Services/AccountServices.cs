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
                //get accounts from dataverse
                HttpResponseMessage accountResponse = await httpClient.GetAsync($"{apiUrl}accounts?$filter=psa_tenantid ne null");

                if (accountResponse.IsSuccessStatusCode || ((int)accountResponse.StatusCode >= 200 && (int)accountResponse.StatusCode <= 209))
                {
                    string accountJsonResponse = await accountResponse.Content.ReadAsStringAsync();
                    dynamic accountJsonObject = JsonConvert.DeserializeObject(accountJsonResponse);

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

                            // Acquire access token for the tenant
                            string LoginAccessToken = await AcquireAccessToken(accountClientId, accountClientSecret, tokenEndpointUrl);

                            if (LoginAccessToken != null)
                            {
                                //getting the Customer Licenses for the current account frpm microsoft api
                                List<CustomerLicensesModel> customerLicenses = new List<CustomerLicensesModel>();
                                // Send the GET request to get the subscribed SKUsFor that Tenant , customerLicenses
                                string apiUrlCustomerLicenses = $"https://graph.microsoft.com/v1.0/subscribedSkus?$select=skuPartNumber,skuId,consumedUnits,prepaidUnits&$filter accountId eq '{tenantId}'";
                                HttpClient httpClientCustomerLicenses = new HttpClient();
                                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);

                                HttpResponseMessage CustomerLicenseResponse = await httpClient.GetAsync(apiUrlCustomerLicenses);

                                if (CustomerLicenseResponse.IsSuccessStatusCode)
                                {

                                    string CustomerLicensejsonResponse = await CustomerLicenseResponse.Content.ReadAsStringAsync();
                                    dynamic CustomerLicenseResult = JsonConvert.DeserializeObject(CustomerLicensejsonResponse);

                                    // Extract the necessary fields and create AccountLicensesModel objects
                                    foreach (var customerLicense in CustomerLicenseResult.value)
                                    {
                                        //Console.WriteLine(customerLicense.prepaidUnits.enabled);
                                        CustomerLicensesModel customerLicenseModel = new CustomerLicensesModel
                                        {
                                            psa_accountName_odata_bind = $"/accounts({accountId})",
                                            psa_accountlicensenumber = accountName + " - " + customerLicense.skuPartNumber,
                                            psa_licenseid = customerLicense.skuId,
                                            psa_quantityassigned = customerLicense.consumedUnits,
                                            psa_quantitypurchased = customerLicense.prepaidUnits.enabled,
                                            // psa_lastlicenserefresh = DateTime.UtcNow.ToString(),
                                            // psa_startdate = DateTime.UtcNow.ToString(),
                                            //psa_enddate = DateTime.UtcNow.ToString(),
                                        };
                                        customerLicenses.Add(customerLicenseModel);
                                    }
                                    Console.WriteLine(customerLicenses.Count);
                                }
                                // Get access token for the accountLicenses table
                                string accountLicenseAccessToken = await dataverseAuth.GetAccessToken();

                                // Create HttpClient for account licenses
                                using HttpClient httpAccountLicenseClient = new HttpClient();

                                // Construct URL for querying account licenses
                                string accountLicenseUrl = $"{apiUrl}psa_accountlicenseses?$filter=_psa_accountname_value eq {accountId}";//output model

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
                                    List<AccountLicensesOutputModel> accountLicensesList = accountLicensesJsonObject.GetValue("value").ToObject<List<AccountLicensesOutputModel>>();

                                    // Loop through each customer License from api
                                    foreach (var customerLicense in customerLicenses)
                                    {

                                        // Serialize the customer license object to JSON
                                        string jsonCustomerLicense = JsonConvert.SerializeObject(customerLicense);
                                        HttpContent createAccountLicenseContent = new StringContent(jsonCustomerLicense, Encoding.UTF8, "application/json");//input model

                                        // Check if the customerLicense is already in the accountlicenses table
                                        if (accountLicensesList.Find(u => u.psa_licenseid == customerLicense.psa_licenseid) == null)
                                        {
                                            // Perform an insert operation
                                            //accountLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_accountlicenseses", createAccountLicenseContent);
                                            HttpRequestMessage createAccountLicenseRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}psa_accountlicenseses");
                                            createAccountLicenseRequest.Headers.Add("Prefer", "return=representation");
                                            createAccountLicenseRequest.Content = createAccountLicenseContent;
                                            HttpResponseMessage createAccountLicenseResponse = await httpAccountLicenseClient.SendAsync(createAccountLicenseRequest);

                                            var createAccountLicenseResponseContent = await createAccountLicenseResponse.Content.ReadAsStringAsync();
                                            var logJson = JsonConvert.DeserializeObject<JObject>(createAccountLicenseResponseContent);
                                            AccountLicensesOutputModel outputAccontLicenseModel = logJson.ToObject<AccountLicensesOutputModel>();
                                            var accountLicenseId = outputAccontLicenseModel.psa_accountlicensesid;

                                            //get users from graph api
                                            List<ContactModel> users = await GetUsersFromGraphApi(httpClient, LoginAccessToken, accountId);

                                            string contactAccessToken = await dataverseAuth.GetAccessToken();
                                            using HttpClient httpContactClient = new HttpClient();

                                            //get contacts from dataverse
                                            string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountId}";

                                            httpContactClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactAccessToken);
                                            HttpResponseMessage contact365Response = await httpContactClient.GetAsync(contactUrl);
                                            //string contactResponseBody = await contact365Response.Content.ReadAsStringAsync();

                                            if (contact365Response.IsSuccessStatusCode || ((int)contact365Response.StatusCode >= 200 && (int)contact365Response.StatusCode <= 209))
                                            {
                                                dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                                                List<ContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<ContactModel>>();//table
                                                var contactId = "";
                                                foreach (var user in users)
                                                {

                                                    string jsonUser = JsonConvert.SerializeObject(user);
                                                    HttpContent createContactContent = new StringContent(jsonUser, Encoding.UTF8, "application/json");

                                                    // Check if the user's email is already in the contacts list
                                                    if (contacts.Find(u => u.emailaddress1 == user.emailaddress1) == null)
                                                    {
                                                        // Perform an insert operation
                                                        //contact365Response = await httpContactClient.PostAsync($"{apiUrl}contacts", createContactContent);
                                                        HttpRequestMessage createContactRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}contacts");
                                                        createContactRequest.Headers.Add("Prefer", "return=representation");
                                                        createContactRequest.Content = createContactContent;
                                                        HttpResponseMessage createContactResponse = await httpContactClient.SendAsync(createContactRequest);

                                                        var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                                                        var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                                                        ContactModel outputContactModel = logJson.ToObject<ContactModel>();
                                                        contactId = outputContactModel.contactid;
                                                        List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                                                        // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                                                        string apiUrlUserLicenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                                                        HttpClient httpClientUserLicenses = new HttpClient();
                                                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);

                                                        HttpResponseMessage UserLicenseResponse = await httpClient.GetAsync(apiUrlUserLicenses);

                                                        if (UserLicenseResponse.IsSuccessStatusCode)
                                                        {
                                                            string UserLicensejsonResponse = await UserLicenseResponse.Content.ReadAsStringAsync();
                                                            dynamic UserLicenseResult = JsonConvert.DeserializeObject(UserLicensejsonResponse);

                                                            // Extract the necessary fields and create UserLicensesModel objects,loop through userLicense List
                                                            foreach (var userLicense in UserLicenseResult.value)
                                                            {
                                                                if (userLicense["userPrincipalName"].ToString().Contains("#EXT#"))
                                                                {
                                                                    continue;
                                                                }
                                                                UserLicensesModel UserLicenseModel = new UserLicensesModel
                                                                {
                                                                    psa_accountLicenseId_odata_bind = $"/psa_accountLicenses({accountLicenseId})",
                                                                    psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                    psa_productstringid = userLicense.assignedPlans.servicePlanId.ToString(),
                                                                };
                                                                userLicenses.Add(UserLicenseModel);
                                                            }
                                                            Console.WriteLine(userLicenses.Count);
                                                            // Get contact licenses from Dataverse
                                                            string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses?$filter=psa_contactprincipalname eq {contactId}";//output model

                                                            // Set authorization header , using same token 
                                                            string contactLicenseAccessToken = await dataverseAuth.GetAccessToken();
                                                            using HttpClient httpContactLicenseClient = new HttpClient();
                                                            httpAccountLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactLicenseAccessToken);

                                                            HttpResponseMessage contactLicenseResponse = await httpAccountLicenseClient.GetAsync(contactLicenseUrl);

                                                            // Check if the request was successful
                                                            if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                            {
                                                                // Deserialize the contact license response JSON
                                                                dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                // Extract the list of contact licenses
                                                                List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                // Loop through each user Licenses
                                                                foreach (var userlisence in userLicenses)
                                                                {

                                                                    // Serialize the userLicense object to JSON
                                                                    string jsonContactLicense = JsonConvert.SerializeObject(userlisence);
                                                                    HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                    // Check if the userLicense is already in the contactlicenses list
                                                                    if (contactLicenses.Find(u => u.psa_productstringid == userlisence.psa_productstringid) == null)
                                                                    {
                                                                        // Perform an insert operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_contactLicenseses", createContactLicenseContent);
                                                                    }
                                                                    else
                                                                    {
                                                                        // Find the existing account license
                                                                        ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u.psa_productstringid == userlisence.psa_productstringid);

                                                                        // Perform an update operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_contactLicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                    }

                                                                    // Read the response body
                                                                    string contactLicenseResponseBody = await contactLicenseResponse.Content.ReadAsStringAsync();
                                                                    Console.WriteLine(contactLicenseResponseBody);

                                                                }
                                                                return new OkObjectResult("Contact Licenses processed successfully");
                                                            }
                                                            else
                                                            {
                                                                return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                            }

                                                        }
                                                    }
                                                    else
                                                    {
                                                        var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                                                        // Perform an update operation
                                                        // contact365Response = await httpContactClient.PatchAsync($"{apiUrl}contacts({contact.contactid})", createContactContent);
                                                        HttpRequestMessage createContactRequest = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}contacts{contact.contactid})");
                                                        createContactRequest.Headers.Add("Prefer", "return=representation");
                                                        createContactRequest.Content = createContactContent;
                                                        HttpResponseMessage createContactResponse = await httpContactClient.SendAsync(createContactRequest);

                                                        var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                                                        var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                                                        ContactModel outputContactModel = logJson.ToObject<ContactModel>();
                                                        contactId = outputContactModel.contactid;
                                                        List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                                                        // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                                                        string apiUrlUserLicenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                                                        HttpClient httpClientUserLicenses = new HttpClient();
                                                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);

                                                        HttpResponseMessage UserLicenseResponse = await httpClient.GetAsync(apiUrlUserLicenses);

                                                        if (UserLicenseResponse.IsSuccessStatusCode)
                                                        {
                                                            string UserLicensejsonResponse = await UserLicenseResponse.Content.ReadAsStringAsync();
                                                            dynamic UserLicenseResult = JsonConvert.DeserializeObject(UserLicensejsonResponse);

                                                            // Extract the necessary fields and create UserLicensesModel objects,loop through userLicense List
                                                            foreach (var userLicense in UserLicenseResult.value)
                                                            {
                                                                if (userLicense["userPrincipalName"].ToString().Contains("#EXT#"))
                                                                {
                                                                    continue;
                                                                }
                                                                UserLicensesModel UserLicenseModel = new UserLicensesModel
                                                                {
                                                                    psa_accountLicenseId_odata_bind = $"/psa_accountLicenses({accountLicenseId})",
                                                                    psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                    psa_productstringid = userLicense.assignedPlans.servicePlanId.ToString(),
                                                                };
                                                                userLicenses.Add(UserLicenseModel);
                                                            }
                                                            Console.WriteLine(userLicenses.Count);
                                                            string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses?$filter=psa_contactprincipalname eq {contactId}";//output model

                                                            // Set authorization header
                                                            httpAccountLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountLicenseAccessToken);

                                                            // Get account licenses from Dataverse
                                                            HttpResponseMessage contactLicenseResponse = await httpAccountLicenseClient.GetAsync(contactLicenseUrl);

                                                            // Check if the request was successful
                                                            if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                            {
                                                                // Deserialize the response JSON
                                                                dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                // Extract the list of contact licenses
                                                                List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                // Loop through each user License
                                                                foreach (var userLicense in userLicenses)
                                                                {

                                                                    // Serialize the user object to JSON
                                                                    string jsonContactLicense = JsonConvert.SerializeObject(userLicense);
                                                                    HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                    // Check if the customerLicense is already in the accountlicenses list
                                                                    if (contactLicenses.Find(u => u.psa_productstringid == userLicense.psa_productstringid) == null)
                                                                    {
                                                                        // Perform an insert operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_contactLicenseses", createContactLicenseContent);
                                                                    }
                                                                    else
                                                                    {
                                                                        // Find the existing account license
                                                                        ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u.psa_productstringid == userLicense.psa_productstringid);

                                                                        // Perform an update operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_contactLicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                    }

                                                                    // Read the response body
                                                                    string contactResponseBody = await contactLicenseResponse.Content.ReadAsStringAsync();
                                                                    Console.WriteLine(contactResponseBody);
                                                                    return new OkObjectResult("Contact Licenses processed successfully");
                                                                }

                                                            }
                                                            else
                                                            {
                                                                return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                            }


                                                        }

                                                    }

                                                }
                                            }
                                            else
                                            {
                                                // Handle token acquisition failure
                                                string errorMessage = await contact365Response.Content.ReadAsStringAsync();
                                                Console.WriteLine($"Error: {errorMessage}");
                                                return new ObjectResult($"Error: {errorMessage}") { StatusCode = StatusCodes.Status500InternalServerError };

                                            }
                                        }
                                        else
                                        {
                                            // Find the existing account license
                                            AccountLicensesOutputModel accountLicense = accountLicensesList.Find(u => u.psa_licenseid == customerLicense.psa_licenseid);
                                            // Perform an update operation
                                            // accountLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_accountlicenseses({accountLicense.psa_accountlicensesid})", createAccountLicenseContent);
                                            HttpRequestMessage updateAccountLicenseRequest = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}psa_accountlicenseses{accountLicense.psa_accountlicensesid})");
                                            updateAccountLicenseRequest.Headers.Add("Prefer", "return=representation");
                                            updateAccountLicenseRequest.Content = createAccountLicenseContent;
                                            HttpResponseMessage updateAccountLicenseResponse = await httpAccountLicenseClient.SendAsync(updateAccountLicenseRequest);

                                            var updateAccountLicenseResponseContent = await updateAccountLicenseResponse.Content.ReadAsStringAsync();
                                            var logJson = JsonConvert.DeserializeObject<JObject>(updateAccountLicenseResponseContent);
                                            AccountLicensesOutputModel outputAccontLicenseModel = logJson.ToObject<AccountLicensesOutputModel>();
                                            //retrieve accountLicenseId
                                            var accountLicenseId = outputAccontLicenseModel.psa_accountlicensesid;

                                            //get users from graph api
                                            List<ContactModel> users = await GetUsersFromGraphApi(httpClient, LoginAccessToken, accountId);

                                            string contactAccessToken = await dataverseAuth.GetAccessToken();
                                            using HttpClient httpContactClient = new HttpClient();

                                            //get contacts from dataverse
                                            string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountId}";

                                            httpContactClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactAccessToken);
                                            HttpResponseMessage contact365Response = await httpContactClient.GetAsync(contactUrl);
                                            //string contactResponseBody = await contact365Response.Content.ReadAsStringAsync();

                                            if (contact365Response.IsSuccessStatusCode || ((int)contact365Response.StatusCode >= 200 && (int)contact365Response.StatusCode <= 209))
                                            {
                                                dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                                                List<ContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<ContactModel>>();//table
                                                var contactId = "";
                                                foreach (var user in users)
                                                {

                                                    string jsonUser = JsonConvert.SerializeObject(user);
                                                    HttpContent createContactContent = new StringContent(jsonUser, Encoding.UTF8, "application/json");

                                                    // Check if the user's email is already in the contacts list
                                                    if (contacts.Find(u => u.emailaddress1 == user.emailaddress1) == null)
                                                    {
                                                        // Perform an insert operation
                                                        //contact365Response = await httpContactClient.PostAsync($"{apiUrl}contacts", createContactContent);
                                                        HttpRequestMessage createContactRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}contacts");
                                                        createContactRequest.Headers.Add("Prefer", "return=representation");
                                                        createContactRequest.Content = createContactContent;
                                                        HttpResponseMessage createContactResponse = await httpContactClient.SendAsync(createContactRequest);

                                                        var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                                                        var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                                                        ContactModel outputContactModel = logJson.ToObject<ContactModel>();
                                                        contactId = outputContactModel.contactid;
                                                        List<UserLicensesModel> userLicensesList = new List<UserLicensesModel>();

                                                        // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                                                        string apiUrlUserLicenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                                                        HttpClient httpClientUserLicenses = new HttpClient();
                                                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);

                                                        HttpResponseMessage UserLicenseResponse = await httpClient.GetAsync(apiUrlUserLicenses);

                                                        if (UserLicenseResponse.IsSuccessStatusCode)
                                                        {
                                                            string UserLicensejsonResponse = await UserLicenseResponse.Content.ReadAsStringAsync();
                                                            dynamic UserLicenseResult = JsonConvert.DeserializeObject(UserLicensejsonResponse);

                                                            // Extract the necessary fields and create UserLicensesModel objects,loop through userLicense List
                                                            foreach (var userLicense in UserLicenseResult.value)
                                                            {
                                                                if (userLicense["userPrincipalName"].ToString().Contains("#EXT#"))
                                                                {
                                                                    continue;
                                                                }
                                                                else
                                                                {
                                                                    UserLicensesModel UserLicenseModel = new UserLicensesModel
                                                                    {
                                                                        psa_accountLicenseId_odata_bind = $"/psa_accountLicenses({accountLicenseId})",
                                                                        psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                        psa_productstringid = userLicense.assignedPlans.servicePlanId.ToString(),
                                                                    };
                                                                    userLicensesList.Add(UserLicenseModel);
                                                                }

                                                            }
                                                            Console.WriteLine(userLicensesList.Count);
                                                            // Get contact licenses from Dataverse
                                                            string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses?$filter=psa_contactprincipalname eq {contactId}";//output model

                                                            // Set authorization header , using same token 
                                                            string contactLicenseAccessToken = await dataverseAuth.GetAccessToken();
                                                            using HttpClient httpContactLicenseClient = new HttpClient();
                                                            httpAccountLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactLicenseAccessToken);

                                                            HttpResponseMessage contactLicenseResponse = await httpAccountLicenseClient.GetAsync(contactLicenseUrl);

                                                            // Check if the request was successful
                                                            if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                            {
                                                                // Deserialize the contact license response JSON
                                                                dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                // Extract the list of contact licenses
                                                                List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                // Loop through each contact License
                                                                foreach (var cl in contactLicenses)
                                                                {

                                                                    // Serialize the userLicense object to JSON
                                                                    string jsonContactLicense = JsonConvert.SerializeObject(cl);
                                                                    HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                    // Check if the contactLicense is already in the accountlicenses list
                                                                    if (contactLicenses.Find(u => u.psa_productstringid == cl.psa_productstringid) == null)
                                                                    {
                                                                        // Perform an insert operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_contactLicenseses", createContactLicenseContent);
                                                                    }
                                                                    else
                                                                    {
                                                                        // Find the existing account license
                                                                        ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u.psa_productstringid == cl.psa_productstringid);

                                                                        // Perform an update operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_contactLicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                    }

                                                                    // Read the response body
                                                                    string contactResponseBody = await contactLicenseResponse.Content.ReadAsStringAsync();
                                                                    Console.WriteLine(contactResponseBody);

                                                                }
                                                                return new OkObjectResult("Contact Licenses processed successfully");
                                                            }
                                                            else
                                                            {
                                                                return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                            }

                                                        }
                                                    }
                                                    else
                                                    {
                                                        var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                                                        // Perform an update operation
                                                        // contact365Response = await httpContactClient.PatchAsync($"{apiUrl}contacts({contact.contactid})", createContactContent);
                                                        HttpRequestMessage updateContactRequest = new HttpRequestMessage(HttpMethod.Patch, $"{apiUrl}contacts{contact.contactid})");
                                                        updateContactRequest.Headers.Add("Prefer", "return=representation");
                                                        updateContactRequest.Content = createContactContent;
                                                        HttpResponseMessage updateContactResponse = await httpContactClient.SendAsync(updateContactRequest);

                                                        var updateContactResponseContent = await updateContactResponse.Content.ReadAsStringAsync();
                                                        var logContactJson = JsonConvert.DeserializeObject<JObject>(updateContactResponseContent);
                                                        ContactModel outputContactModel = logJson.ToObject<ContactModel>();
                                                        contactId = outputContactModel.contactid;
                                                        List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                                                        // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                                                        string apiUrlUserLicenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                                                        HttpClient httpClientUserLicenses = new HttpClient();
                                                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);

                                                        HttpResponseMessage UserLicenseResponse = await httpClient.GetAsync(apiUrlUserLicenses);

                                                        if (UserLicenseResponse.IsSuccessStatusCode)
                                                        {
                                                            string UserLicensejsonResponse = await UserLicenseResponse.Content.ReadAsStringAsync();
                                                            dynamic UserLicenseResult = JsonConvert.DeserializeObject(UserLicensejsonResponse);

                                                            // Extract the necessary fields and create UserLicensesModel objects,loop through userLicense List
                                                            foreach (var userLicense in UserLicenseResult.value)
                                                            {
                                                                if (userLicense["userPrincipalName"].ToString().Contains("#EXT#"))
                                                                {
                                                                    continue;
                                                                }
                                                                UserLicensesModel UserLicenseModel = new UserLicensesModel
                                                                {
                                                                    psa_accountLicenseId_odata_bind = $"/psa_accountLicenses({accountLicenseId})",
                                                                    psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                    psa_productstringid = userLicense.assignedPlans.servicePlanId.ToString(),
                                                                };
                                                                userLicenses.Add(UserLicenseModel);
                                                            }
                                                            Console.WriteLine(userLicenses.Count);
                                                            string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses?$filter=psa_contactprincipalname eq {contactId}";//output model

                                                            // Set authorization header
                                                            httpAccountLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accountLicenseAccessToken);

                                                            // Get account licenses from Dataverse
                                                            HttpResponseMessage contactLicenseResponse = await httpAccountLicenseClient.GetAsync(contactLicenseUrl);

                                                            // Check if the request was successful
                                                            if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                            {
                                                                // Deserialize the response JSON
                                                                dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                // Extract the list of contact licenses
                                                                List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                // Loop through each user License
                                                                foreach (var userLicense in userLicenses)
                                                                {

                                                                    // Serialize the user object to JSON
                                                                    string jsonContactLicense = JsonConvert.SerializeObject(userLicense);
                                                                    HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                    // Check if the customerLicense is already in the accountlicenses list
                                                                    if (contactLicenses.Find(u => u.psa_productstringid == userLicense.psa_productstringid) == null)
                                                                    {
                                                                        // Perform an insert operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_contactLicenseses", createContactLicenseContent);
                                                                    }
                                                                    else
                                                                    {
                                                                        // Find the existing account license
                                                                        ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u.psa_productstringid == userLicense.psa_productstringid);

                                                                        // Perform an update operation
                                                                        contactLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_contactLicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                    }

                                                                    // Read the response body
                                                                    string contactResponseBody = await contactLicenseResponse.Content.ReadAsStringAsync();
                                                                    Console.WriteLine(contactResponseBody);
                                                                    return new OkObjectResult("Contact Licenses processed successfully");
                                                                }

                                                            }
                                                            else
                                                            {
                                                                return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                            }


                                                        }

                                                    }

                                                }
                                            }
                                            else
                                            {
                                                // Handle token acquisition failure
                                                string errorMessage = await contact365Response.Content.ReadAsStringAsync();
                                                Console.WriteLine($"Error: {errorMessage}");
                                                return new ObjectResult($"Error: {errorMessage}") { StatusCode = StatusCodes.Status500InternalServerError };

                                            }

                                        }

                                        // Read the response body
                                        string responseBody = await accountLicenseResponse.Content.ReadAsStringAsync();
                                        Console.WriteLine(responseBody);
                                    }
                                }
                                else
                                {
                                    // Handle token acquisition failure
                                    return new ObjectResult("Failed to retrieve account licenses from dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                }
                            }
                            else
                            {
                                // Handle token acquisition failure
                                return new ObjectResult("Failed to acquire access token for the tenant") { StatusCode = StatusCodes.Status500InternalServerError };
                            }

                            return new OkObjectResult("Accounts processed successfully");
                        }
                    }
                    else
                    {
                        return new ObjectResult("Account JSON object is null or empty") { StatusCode = StatusCodes.Status400BadRequest };
                    }
                }
                else
                {
                    return new ObjectResult("Failed to retrieve accounts from dataverse") { StatusCode = (int)accountResponse.StatusCode };
                }
                return null;
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

        public async Task<List<ContactModel>> GetUsersFromGraphApi(HttpClient httpClient, string accessToken, string accountId)
        {
            try
            {
                string graphApiUrl = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";

                // Set the Authorization header with the access token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, graphApiUrl);
                request.Headers.Add("ConsistencyLevel", "eventual");
                HttpResponseMessage usersResponse = await httpClient.SendAsync(request);


                if (usersResponse.IsSuccessStatusCode || ((int)usersResponse.StatusCode >= 200 && (int)usersResponse.StatusCode <= 209))
                {
                    string usersResponseBody = await usersResponse.Content.ReadAsStringAsync();
                    dynamic usersJsonObject = JsonConvert.DeserializeObject(usersResponseBody);


                    List<ContactModel> users = new List<ContactModel>();
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

                            var user = new ContactModel
                            {
                                parentcustomerid_account_odata_bind = $"/accounts({accountId})",
                                yomifullname = userRecord.displayName,
                                firstname = userFirstName,
                                lastname = userLastName,
                                emailaddress1 = userRecord.mail,
                                adx_identity_username = userRecord.userPrincipalName,

                            };
                            users.Add(user);
                            Console.WriteLine(user);
                        }
                    }
                    //return list of contacts
                    Console.WriteLine(users.Count);//hii
                    return users;

                }
                else
                {
                    // Log or handle user retrieval failure
                    return null;
                }
            }
            catch (Exception)
            {
                // Log or handle any exceptions

                return new List<ContactModel>();
            }
        }
    }
}


