using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.AccountLicenses
{
    public class CustomerLicensesModel
    {
        [JsonProperty("psa_accountName@odata.bind")]
        public string psa_accountName_odata_bind { get; set; } = "";
        // public string psa_lastlicenserefresh { get; set; } = ""; fill it business rule with modified date
        public string psa_licenseid { get; set; } = "";//skuId
        public string psa_accountlicensenumber { get; set; } = "";//skuParNumber,primary field 
       // public string psa_accountlicensesid { get; set; } = "";//unique id of the table
        public string psa_quantityassigned { get; set; } = "";//consumedUnits
        public string psa_quantitypurchased { get; set; } = "";//enabled
        //public string psa_lastlicenserefresh { get; set; } = "";//utc now
        //public string psa_startdate { get; set; } = "";
        //public string psa_enddate { get; set; } = "";
    }
}
