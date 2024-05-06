using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.ContactLicenses
{
    public class ContactLicensesOutputModel
    {
        public string _psa_accountLicenseId_value { get; set; } = "";//lookup at table 
        
        public string _psa_ContactPrincipalName_value{ get; set; } = "";//lookup at table contact

        public string psa_productstringid { get; set; } = "";//id of license for now string after lookup    ,//assignedPlans.servicePlanId
        public string psa_contactlicensesid { get; set; } = "";//unique id of the table
                                                            
    }
}
