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
using Microsoft.CodeAnalysis.VisualBasic.Syntax;


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

                            // Acquire access token for the tenant in micros
                            string LoginAccessToken = await AcquireAccessToken(accountClientId, accountClientSecret, tokenEndpointUrl);

                            if (LoginAccessToken != null)
                            {
                                string M365ProductsAccessToken = await dataverseAuth.GetAccessToken();

                                using HttpClient httpM365ProductsClient = new HttpClient();
                                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", M365ProductsAccessToken);
                                //get M365 Product for that customer License from dataverse
                                HttpResponseMessage M365ProductsResponse = await httpClient.GetAsync($"{apiUrl}psa_m365productses");

                                if (M365ProductsResponse.IsSuccessStatusCode || ((int)M365ProductsResponse.StatusCode >= 200 && (int)M365ProductsResponse.StatusCode <= 209))
                                {
                                    string m365ProductResponse = await M365ProductsResponse.Content.ReadAsStringAsync();
                                    dynamic m365ProductsJsonObject = JsonConvert.DeserializeObject(m365ProductResponse);

                                    // Deserialize the JSON array into a list of M365ProductsModel
                                    List<M365ProductsModel> m365ProductList = JsonConvert.DeserializeObject<List<M365ProductsModel>>(m365ProductsJsonObject.value.ToString());

                                    //getting the Customer Licenses for the current account frpm microsoft api
                                    List<CustomerLicensesModel> customerLicenses = new List<CustomerLicensesModel>();

                                    // Send the GET request to get the subscribed SKUsFor that Tenant , customerLicenses
                                    string apiUrlCustomerLicenses = $"https://graph.microsoft.com/v1.0/subscribedSkus?$select=skuPartNumber,skuId,consumedUnits,prepaidUnits&$filter accountId eq '{tenantId}'";
                                    HttpClient httpClientCustomerLicenses = new HttpClient();
                                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                                    httpClient.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");

                                    HttpResponseMessage CustomerLicenseResponse = await httpClient.GetAsync(apiUrlCustomerLicenses);

                                    if (CustomerLicenseResponse.IsSuccessStatusCode)
                                    {
                                        string CustomerLicensejsonResponse = await CustomerLicenseResponse.Content.ReadAsStringAsync();
                                        dynamic CustomerLicenseResult = JsonConvert.DeserializeObject(CustomerLicensejsonResponse);

                                        // Extract the necessary fields and create AccountLicensesModel objects
                                        foreach (var customerLicense in CustomerLicenseResult.value)
                                        {
                                            var matchingProduct = m365ProductList.FirstOrDefault(product => product.psa_guid == customerLicense.skuId.ToString());

                                            if (matchingProduct != null)
                                            {
                                                var m365ProductId = matchingProduct.psa_m365productsid;
                                                CustomerLicensesModel customerLicenseModel = new CustomerLicensesModel
                                                {
                                                    psa_accountName_odata_bind = $"/accounts({accountId})",
                                                    psa_accountlicensenumber = accountName + " - " + customerLicense.skuPartNumber,
                                                    psa_licenseid = customerLicense.skuId, //guid
                                                    psa_quantityassigned = customerLicense.consumedUnits,
                                                    psa_quantitypurchased = customerLicense.prepaidUnits.enabled,
                                                    //psa_lastlicenserefresh = DateTime.UtcNow.ToString(),
                                                    //psa_startdate = DateTime.UtcNow.ToString(),
                                                    //psa_enddate = DateTime.UtcNow.ToString(),
                                                    psa_ProductStringId_odata_bind = $"/psa_m365productses({m365ProductId})"
                                                };
                                                customerLicenses.Add(customerLicenseModel);
                                            }
                                            else
                                            {
                                                continue;
                                            }
                                             
                                            
                                        }


                                    }
                                    else
                                    {
                                        return new ObjectResult("Failed to get customer licenses from graph api") { StatusCode = StatusCodes.Status500InternalServerError };
                                    }


                                    // Get access token for the accountLicenses table
                                    string accountLicenseAccessToken = await dataverseAuth.GetAccessToken();

                                    // Create HttpClient for account licenses
                                    using HttpClient httpAccountLicenseClient = new HttpClient();

                                    // get account licenses for that account 
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

                                        // Extract the list of account licenses from dataverse and put it in account licenses list
                                        List<AccountLicensesOutputModel> accountLicenses = accountLicensesJsonObject.GetValue("value").ToObject<List<AccountLicensesOutputModel>>();

                                        // Loop through each customer License from api
                                        foreach (var customerLicense in customerLicenses)//32
                                        {
                                           var nb=customerLicenses.Count();
                                            Console.WriteLine("Nbr of the customer licenses is :{nb}");
                                            // Serialize the customer license object to JSON
                                            string jsonCustomerLicense = JsonConvert.SerializeObject(customerLicense);
                                            HttpContent createAccountLicenseContent = new StringContent(jsonCustomerLicense, Encoding.UTF8, "application/json");//input model

                                            var licenseId = customerLicense.psa_licenseid;
                                            // Check if the customerLicense is already in the accountlicenses table
                                            if (accountLicenses.Find(u => u.psa_licenseid == licenseId) == null)
                                            {
                                                // Perform an insert operation
                                                // accountLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_accountlicenseses", createAccountLicenseContent);
                                                accountLicenseResponse = await httpAccountLicenseClient.PostAsync($"{apiUrl}psa_accountlicenseses", createAccountLicenseContent);
                                                HttpRequestMessage createAccountLicenseRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiUrl}psa_accountlicenseses");
                                                createAccountLicenseRequest.Headers.Add("Prefer", "return=representation");
                                                createAccountLicenseRequest.Headers.Add("ConsistencyLevel", "eventual");
                                                createAccountLicenseRequest.Content = createAccountLicenseContent;

                                                HttpResponseMessage createAccountLicenseResponse = await httpAccountLicenseClient.SendAsync(createAccountLicenseRequest);


                                                var createAccountLicenseResponseContent = await createAccountLicenseResponse.Content.ReadAsStringAsync();
                                                var logJson = JsonConvert.DeserializeObject<JObject>(createAccountLicenseResponseContent);
                                                AccountLicensesOutputModel outputAccountLicenseModel = logJson.ToObject<AccountLicensesOutputModel>();
                                                var accountLicenseId = outputAccountLicenseModel.psa_accountlicensesid;

                                                //get contacts from dataverse
                                                string contactAccessToken = await dataverseAuth.GetAccessToken();
                                                using HttpClient httpContactClient = new HttpClient();
                                                string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountId}";

                                                httpContactClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactAccessToken);
                                                HttpResponseMessage contact365Response = await httpContactClient.GetAsync(contactUrl);
                                                //string contactResponseBody = await contact365Response.Content.ReadAsStringAsync();

                                                if (contact365Response.IsSuccessStatusCode || ((int)contact365Response.StatusCode >= 200 && (int)contact365Response.StatusCode <= 209))
                                                {
                                                    dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                                                    List<OutputContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<OutputContactModel>>();//table
                                                    var contactId = "";

                                                    List<User_LicensesModel> user_Licenses = new List<User_LicensesModel>();
                                                    List<UsersModel> users = new List<UsersModel>();
                                                    List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                                                    // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                                                    string apiUrlUser_Licenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                                                    HttpClient httpClientUserLicenses = new HttpClient();
                                                    httpClientUserLicenses.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                                                    httpClientUserLicenses.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
                                                    HttpResponseMessage User_LicenseResponse = await httpClientUserLicenses.GetAsync(apiUrlUser_Licenses);

                                                    if (User_LicenseResponse.IsSuccessStatusCode)
                                                    {

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
                                                            if (user_License["assignedLicenses"] == null || user_License["assignedLicenses"].Count == 0)
                                                            {
                                                                // Skip processing users with no assigned licenses
                                                                continue;
                                                            }
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
                                                            };
                                                            // Add the license model to the list
                                                            users.Add(user);
                                                            // Count the number of assigned licenses
                                                            //int numberOfLicenses = user_License["assignedLicenses"].Count;

                                                            //// Print the count
                                                            //Console.WriteLine($"Number of assigned licenses for {userFirstName} {userLastName}: {numberOfLicenses}");
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

                                                                HttpResponseMessage createContactResponse = await httpContactClient.SendAsync(createContactRequest);

                                                                var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                                                                var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                                                                OutputContactModel outputContactModel = logContactJson.ToObject<OutputContactModel>();
                                                                contactId = outputContactModel.contactid;
                                                                foreach (var license in user_License.assignedLicenses)
                                                                {
                                                                    var nbr = user_License.assignedLicenses.Count;
                                                                    Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                                                                    var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                                                                    var accountlicense = accountLicenses.FirstOrDefault(u => u.psa_licenseid.ToString() == license.skuId?.ToString());

                                                                    if (m365product != null && accountlicense != null)
                                                                    {
                                                                        var m365productId = m365product.psa_m365productsid;
                                                                        var accountlicenseid = accountlicense.psa_accountlicensesid;
                                                                        // Create UserLicensesModel object for each assigned license
                                                                        UserLicensesModel userLicenseModel = new UserLicensesModel
                                                                        {
                                                                            psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                            psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                                                            psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                                                                        };
                                                                        string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses";//$filter=_psa_contactprincipalname_value eq {contactId}";//output model

                                                                        // Set authorization header 
                                                                        string contactLicenseAccessToken = await dataverseAuth.GetAccessToken();
                                                                        using HttpClient httpContactLicenseClient = new HttpClient();
                                                                        httpContactLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactLicenseAccessToken);

                                                                        HttpResponseMessage contactLicenseResponse = await httpContactLicenseClient.GetAsync(contactLicenseUrl);

                                                                        string r = await contactLicenseResponse.Content.ReadAsStringAsync();

                                                                        // Check if the request was successful
                                                                        if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                                        {
                                                                            // Deserialize the contact license response JSON
                                                                            dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                            // Extract the list of contact licenses
                                                                            List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                            // Serialize the userLicense object to JSON
                                                                            string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                                                                            HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                            // Check if the contactLicense is already in the contactLicenses list
                                                                            if (contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid) == null)
                                                                            {
                                                                                // Perform an insert operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                // Find the existing account license
                                                                                ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid);

                                                                                // Perform an update operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PatchAsync($"{apiUrl}psa_contactlicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }

                                                                            }

                                                                        }
                                                                        else
                                                                        {
                                                                            return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        continue;
                                                                    }
                                                                }
                                                            }
                                                            else
                                                            {
                                                                var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                                                                contactId = contact.contactid;
                                                                // Perform an update operation
                                                                contact365Response = await httpContactClient.PatchAsync($"{apiUrl}contacts({contactId})", createContactContent);
                                                                foreach (var license in user_License.assignedLicenses)
                                                                {
                                                                    var nbr = user_License.assignedLicenses.Count;
                                                                    Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                                                                    var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                                                                    var accountlicense = accountLicenses.FirstOrDefault(u => u.psa_licenseid.ToString() == license.skuId?.ToString());

                                                                    if (m365product != null && accountlicense != null)
                                                                    {
                                                                        var m365productId = m365product.psa_m365productsid;
                                                                        var accountlicenseid = accountlicense.psa_accountlicensesid;
                                                                        // Create UserLicensesModel object for each assigned license
                                                                        UserLicensesModel userLicenseModel = new UserLicensesModel
                                                                        {
                                                                            psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                            psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                                                            psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                                                                        };
                                                                        string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses";//$filter=_psa_contactprincipalname_value eq {contactId}";//output model

                                                                        // Set authorization header 
                                                                        string contactLicenseAccessToken = await dataverseAuth.GetAccessToken();
                                                                        using HttpClient httpContactLicenseClient = new HttpClient();
                                                                        httpContactLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactLicenseAccessToken);

                                                                        HttpResponseMessage contactLicenseResponse = await httpContactLicenseClient.GetAsync(contactLicenseUrl);

                                                                        string r = await contactLicenseResponse.Content.ReadAsStringAsync();

                                                                        // Check if the request was successful
                                                                        if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                                        {
                                                                            // Deserialize the contact license response JSON
                                                                            dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                            // Extract the list of contact licenses
                                                                            List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                            // Serialize the userLicense object to JSON
                                                                            string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                                                                            HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                            // Check if the contactLicense is already in the contactLicenses list
                                                                            if (contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid) == null)
                                                                            {
                                                                                // Perform an insert operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                // Find the existing account license
                                                                                ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid);

                                                                                // Perform an update operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PatchAsync($"{apiUrl}psa_contactlicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }

                                                                            }

                                                                        }
                                                                        else
                                                                        {
                                                                            return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        continue;
                                                                    }
                                                                }


                                                            }

                                                        }

                                                        Console.WriteLine(users.Count);

                                                    }

                                                    else
                                                    {

                                                        return new ObjectResult("Failed to retrieve user licenses from Graph api") { StatusCode = StatusCodes.Status500InternalServerError };

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
                                                AccountLicensesOutputModel accountLicense = accountLicenses.Find(u => u.psa_licenseid == licenseId);
                                                string accountLicenseId = accountLicense.psa_accountlicensesid;
                                                // Perform an update operation
                                                accountLicenseResponse = await httpAccountLicenseClient.PatchAsync($"{apiUrl}psa_accountlicenseses({accountLicenseId})", createAccountLicenseContent);

                                                //get contacts from dataverse
                                                string contactAccessToken = await dataverseAuth.GetAccessToken();
                                                using HttpClient httpContactClient = new HttpClient();
                                                string contactUrl = $"{apiUrl}contacts?$filter=_parentcustomerid_value eq {accountId}";

                                                httpContactClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactAccessToken);
                                                HttpResponseMessage contact365Response = await httpContactClient.GetAsync(contactUrl);
                                                //string contactResponseBody = await contact365Response.Content.ReadAsStringAsync();

                                                if (contact365Response.IsSuccessStatusCode || ((int)contact365Response.StatusCode >= 200 && (int)contact365Response.StatusCode <= 209))
                                                {
                                                    dynamic contactsJsonObject = JsonConvert.DeserializeObject(await contact365Response.Content.ReadAsStringAsync());

                                                    List<OutputContactModel> contacts = contactsJsonObject.GetValue("value").ToObject<List<OutputContactModel>>();//table
                                                    var contactId = "";

                                                    List<User_LicensesModel> user_Licenses = new List<User_LicensesModel>();
                                                    List<UsersModel> users = new List<UsersModel>();
                                                    List<UserLicensesModel> userLicenses = new List<UserLicensesModel>();

                                                    // Send the GET request to get the subscribed SKUsFor that Tenant,userLicenses
                                                    string apiUrlUser_Licenses = $"https://graph.microsoft.com/v1.0/users?$filter=mail ne null&$top=999&$count=true&&$select=id,username,userPrincipalName,givenName,surname,displayName,mail,assignedLicenses,assignedPlans";
                                                    HttpClient httpClientUserLicenses = new HttpClient();
                                                    httpClientUserLicenses.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LoginAccessToken);
                                                    httpClientUserLicenses.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
                                                    HttpResponseMessage User_LicenseResponse = await httpClientUserLicenses.GetAsync(apiUrlUser_Licenses);

                                                    if (User_LicenseResponse.IsSuccessStatusCode)
                                                    {

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
                                                            if (user_License["assignedLicenses"] == null || user_License["assignedLicenses"].Count == 0)
                                                            {
                                                                // Skip processing users with no assigned licenses
                                                                continue;
                                                            }
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
                                                            };
                                                            // Add the license model to the list
                                                            users.Add(user);
                                                            // Count the number of assigned licenses
                                                            //int numberOfLicenses = user_License["assignedLicenses"].Count;

                                                            //// Print the count
                                                            //Console.WriteLine($"Number of assigned licenses for {userFirstName} {userLastName}: {numberOfLicenses}");
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

                                                                HttpResponseMessage createContactResponse = await httpContactClient.SendAsync(createContactRequest);

                                                                var createContactResponseContent = await createContactResponse.Content.ReadAsStringAsync();
                                                                var logContactJson = JsonConvert.DeserializeObject<JObject>(createContactResponseContent);
                                                                OutputContactModel outputContactModel = logContactJson.ToObject<OutputContactModel>();
                                                                contactId = outputContactModel.contactid;
                                                                foreach (var license in user_License.assignedLicenses)
                                                                {
                                                                    var nbr = user_License.assignedLicenses.Count;
                                                                    Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                                                                    var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                                                                    var accountlicense = accountLicenses.FirstOrDefault(u => u.psa_licenseid.ToString() == license.skuId?.ToString());

                                                                    if (m365product != null && accountlicense != null)
                                                                    {
                                                                        var m365productId = m365product.psa_m365productsid;
                                                                        var accountlicenseid = accountlicense.psa_accountlicensesid;
                                                                        // Create UserLicensesModel object for each assigned license
                                                                        UserLicensesModel userLicenseModel = new UserLicensesModel
                                                                        {
                                                                            psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                            psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                                                            psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                                                                        };
                                                                        string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses";//$filter=_psa_contactprincipalname_value eq {contactId}";//output model

                                                                        // Set authorization header 
                                                                        string contactLicenseAccessToken = await dataverseAuth.GetAccessToken();
                                                                        using HttpClient httpContactLicenseClient = new HttpClient();
                                                                        httpContactLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactLicenseAccessToken);

                                                                        HttpResponseMessage contactLicenseResponse = await httpContactLicenseClient.GetAsync(contactLicenseUrl);

                                                                        string r = await contactLicenseResponse.Content.ReadAsStringAsync();

                                                                        // Check if the request was successful
                                                                        if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                                        {
                                                                            // Deserialize the contact license response JSON
                                                                            dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                            // Extract the list of contact licenses
                                                                            List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                            // Serialize the userLicense object to JSON
                                                                            string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                                                                            HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                            // Check if the contactLicense is already in the contactLicenses list
                                                                            if (contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid) == null)
                                                                            {
                                                                                // Perform an insert operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                // Find the existing account license
                                                                                ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid);

                                                                                // Perform an update operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PatchAsync($"{apiUrl}psa_contactlicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }

                                                                            }

                                                                        }
                                                                        else
                                                                        {
                                                                            return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        continue;
                                                                    }
                                                                }
                                                            }
                                                            else
                                                            {
                                                                var contact = contacts.Find(u => u.emailaddress1 == user.emailaddress1);
                                                                contactId = contact.contactid;
                                                                // Perform an update operation
                                                                contact365Response = await httpContactClient.PatchAsync($"{apiUrl}contacts({contactId})", createContactContent);
                                                                foreach (var license in user_License.assignedLicenses)
                                                                {
                                                                    var nbr = user_License.assignedLicenses.Count;
                                                                    Console.WriteLine($"Number of assigned licenses for {user.adx_identity_username} is {nbr}");
                                                                    var m365product = m365ProductList.FirstOrDefault(u => u.psa_guid?.ToString() == license.skuId?.ToString());

                                                                    var accountlicense = accountLicenses.FirstOrDefault(u => u.psa_licenseid.ToString() == license.skuId?.ToString());
                                                                    
                                                                    if (m365product!=null && accountlicense != null)
                                                                    {
                                                                        var m365productId = m365product.psa_m365productsid;
                                                                        var accountlicenseid = accountlicense.psa_accountlicensesid;
                                                                        // Create UserLicensesModel object for each assigned license
                                                                        UserLicensesModel userLicenseModel = new UserLicensesModel
                                                                        {
                                                                            psa_ContactPrincipalName_odata_bind = $"/contacts({contactId})",
                                                                            psa_ProductStringId_odata_bind = $"/psa_m365productses({m365productId})",
                                                                            psa_AccountLicenseId_odata_bind = $"/psa_accountlicenseses({accountlicenseid})"
                                                                        };
                                                                        string contactLicenseUrl = $"{apiUrl}psa_contactlicenseses";//$filter=_psa_contactprincipalname_value eq {contactId}";//output model

                                                                        // Set authorization header 
                                                                        string contactLicenseAccessToken = await dataverseAuth.GetAccessToken();
                                                                        using HttpClient httpContactLicenseClient = new HttpClient();
                                                                        httpContactLicenseClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", contactLicenseAccessToken);

                                                                        HttpResponseMessage contactLicenseResponse = await httpContactLicenseClient.GetAsync(contactLicenseUrl);

                                                                        string r = await contactLicenseResponse.Content.ReadAsStringAsync();

                                                                        // Check if the request was successful
                                                                        if (contactLicenseResponse.IsSuccessStatusCode || ((int)contactLicenseResponse.StatusCode >= 200 && (int)contactLicenseResponse.StatusCode <= 209))
                                                                        {
                                                                            // Deserialize the contact license response JSON
                                                                            dynamic contactLicensesJsonObject = JsonConvert.DeserializeObject(await contactLicenseResponse.Content.ReadAsStringAsync());

                                                                            // Extract the list of contact licenses
                                                                            List<ContactLicensesOutputModel> contactLicenses = contactLicensesJsonObject.GetValue("value").ToObject<List<ContactLicensesOutputModel>>();

                                                                            // Serialize the userLicense object to JSON
                                                                            string jsonContactLicense = JsonConvert.SerializeObject(userLicenseModel);
                                                                            HttpContent createContactLicenseContent = new StringContent(jsonContactLicense, Encoding.UTF8, "application/json");//input model

                                                                            // Check if the contactLicense is already in the contactLicenses list
                                                                            if (contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId  &&  u._psa_accountlicenseid_value == accountlicenseid) == null)
                                                                            {
                                                                                // Perform an insert operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PostAsync($"{apiUrl}psa_contactlicenseses", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {
                                                                                   
                                                                                    // write erro to log file
                                                                                }
                                                                            }
                                                                            else
                                                                            {
                                                                                // Find the existing account license
                                                                                ContactLicensesOutputModel contactLicense = contactLicenses.Find(u => u._psa_productstringid_value == m365productId && u._psa_contactprincipalname_value == contactId && u._psa_accountlicenseid_value == accountlicenseid);

                                                                                // Perform an update operation
                                                                                contactLicenseResponse = await httpContactLicenseClient.PatchAsync($"{apiUrl}psa_contactlicenseses({contactLicense.psa_contactlicensesid})", createContactLicenseContent);
                                                                                if (!contactLicenseResponse.IsSuccessStatusCode)
                                                                                {

                                                                                    // write erro to log file
                                                                                }

                                                                            }

                                                                        }
                                                                        else
                                                                        {
                                                                            return new ObjectResult("Failed to retrieve Contact Licenses from Dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        continue;
                                                                    }
                                                                }


                                                            }

                                                        }

                                                        Console.WriteLine(users.Count);

                                                    }

                                                    else
                                                    {

                                                        return new ObjectResult("Failed to retrieve user licenses from Graph api") { StatusCode = StatusCodes.Status500InternalServerError };

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
                                        }
                                        return new OkObjectResult("Accounts Licenses processed successfully");
                                    }
                                    else
                                    {
                                        // Handle token acquisition failure
                                        return new ObjectResult("Failed to retrieve account licenses from dataverse") { StatusCode = StatusCodes.Status500InternalServerError };
                                    }
                                }
                                else
                                {
                                    return new ObjectResult("Failed to acquire M365 Products") { StatusCode = StatusCodes.Status500InternalServerError };
                                }
                            }
                            else
                            {
                                // Handle token acquisition failure
                                return new ObjectResult("Failed to acquire access token for the tenant") { StatusCode = StatusCodes.Status500InternalServerError };
                            }

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
                return new ObjectResult("something went wrong!");
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
    }
}
