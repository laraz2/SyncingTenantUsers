using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.User_Licenses
{
    public class User_LicensesModel
    {
        //public string contactid { get; set; } = "";
        public string firstname { get; set; } = "";

        public string lastname { get; set; } = "";
        public string yomifullname { get; set; } = "";
        //public string fullname { get; set; } = "";
        //public string jobtitle { get; set; } = "";

        [JsonProperty("parentcustomerid_account@odata.bind")]

        public string parentcustomerid_account_odata_bind { get; set; }
        public string adx_identity_username { get; set; } = "";

        public string emailaddress1 { get; set; } = "";
        [JsonProperty("psa_ContactPrincipalName@odata.bind")]
        public string psa_ContactPrincipalName_odata_bind { get; set; } = "";//lookup at table contact
        [JsonProperty("psa_ProductStringId@odata.bind")]
        public string psa_ProductStringId_odata_bind { get; set; } = "";//lookup to table product Licenses

        public  List<AssignedLicenseModel> assignedLicenses  { get; set; } =null;   
    }
}
