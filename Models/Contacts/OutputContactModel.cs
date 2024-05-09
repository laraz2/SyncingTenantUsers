using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using System.Xml;
using Newtonsoft.Json;

namespace SyncingTenantUsers.Models.Contacts
{
    public class OutputContactModel
    {
        public string contactid { get; set; } = "";
        public string firstname { get; set; } = "";
        
        public string lastname { get; set; } = "";
        public string yomifullname { get; set; } = "";
        public string fullname { get; set; } = "";
        //public string jobtitle { get; set; } = "";

        public string _parentcustomerid_value { get; set; }
        public string adx_identity_username { get; set; } = "";

        public string emailaddress1 { get; set; } = "";
        
        
    }
}
