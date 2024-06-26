﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.ContactLicenses
{
    public class ContactLicensesInputModel
    {
        [JsonProperty("psa_ContactPrincipalName@odata.bind")]
        public string psa_ContactPrincipalName_odata_bind { get; set; } = "";//lookup at table contact
        [JsonProperty("psa_ProductStringId@odata.bind")]
        public string psa_ProductStringId_odata_bind { get; set; } = "";//lookup to table product Licenses
        [JsonProperty("psa_AccountLicenseId@odata.bind")]
        public string psa_AccountLicenseId_odata_bind { get; set; } = "";//lookup at table accountLicenses

    }
}
