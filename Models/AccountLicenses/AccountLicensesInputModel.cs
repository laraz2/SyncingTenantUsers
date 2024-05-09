using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.AccountLicenses
{
    public class AccountLicensesInputModel
    {
        [JsonProperty("psa_accountName@odata.bind")]
        public string psa_accountName_odata_bind { get; set; } = "";
        // public string psa_lastlicenserefresh { get; set; } = ""; fill it business rule with modified date
        [JsonProperty("psa_ProductStringId@odata.bind")]
        public string psa_ProductStringId_odata_bind { get; set; } = "";
        public string psa_licenseid { get; set; } = "";//skuId from graph api== guid from m365 products
        public string psa_accountlicensenumber { get; set; } = "";//skuPartNumber,primary field in table account
        //public string psa_accountlicensesid { get; set; } = "";//unique id of the table
        public string psa_quantityassigned { get; set; } = "";//consumedUnits
        public string psa_quantitypurchased { get; set; } = "";//enabled
        //public string psa_lastlicenserefresh { get; set; } = "";//utc now
        //public string psa_startdate { get; set; } = "";
        //public string psa_enddate { get; set; } = "";
       
    }
}
