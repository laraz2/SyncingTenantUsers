using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncingTenantUsers.Models.M365_Products
{
    public class M365ProductsModel
    {
        public string psa_guid { get; set; } = ""; //skuId from graph api

        public string psa_m365productsid { get; set; } = "";//unique id of the table m365 products
        public string psa_productdisplayname { get; set; } = ""; //to be shown in table contact licenses and accountLicenses
       
    }
}
