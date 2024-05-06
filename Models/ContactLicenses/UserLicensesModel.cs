using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.ContactLicenses
{
    public class UserLicensesModel
    {
        [JsonProperty("psa_accountLicenseId@odata.bind")]
        public string psa_accountLicenseId_odata_bind { get; set; } = "";//lookup at table account Licenses  ,//assignedLicenses.skuId
        [JsonProperty("psa_ContactPrincipalName@odata.bind")]
        public string psa_ContactPrincipalName_odata_bind { get; set; } = "";//lookup at table contact

        public string psa_productstringid { get; set; } = "";//id of license for now string after lookup    ,//assignedPlans.servicePlanId
        public string psa_contactlicensesid { get; set; } = "";//unique id field of the table
    }
}
