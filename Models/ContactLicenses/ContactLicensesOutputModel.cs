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
        
        
        public string _psa_contactprincipalname_value { get; set; } = "";//lookup at table contact
        
        public string _psa_productstringid_value { get; set; } = "";//lookup to table m365 products
        public string _psa_accountlicenseid_value { get; set; } = "";//lookup to table account Licenses
        public string psa_contactlicensesid { get; set; } = "";//unique id of the table
                                                            
    }
}
