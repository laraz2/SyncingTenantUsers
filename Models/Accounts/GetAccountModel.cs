using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.Accounts
{
    public class GetAccountModel
    {

        public string name { get; set; } = "";
        public string psa_tenantid { get; set; } = "";
        public string psa_clientid { get; set; } = "";
        public string psa_clientsecret { get; set; } = "";
    }
}
