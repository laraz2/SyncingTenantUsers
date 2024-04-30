using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.Logs
{
    public class InputLogDetailModel
    {
        public string psa_title { get; set; }

        public string psa_additionalinformation { get; set; }

        [JsonProperty("psa_Log@odata.bind")]
        public string psa_Log_odata_bind { get; set; }


        public string psa_errormessage { get; set; }

        public string psa_status { get; set; }

        public string psa_statuscode { get; set; }


    }
}

