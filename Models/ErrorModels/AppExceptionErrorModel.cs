namespace SyncingTenantUsers.Models.ErrorModels
{
    public class AppExceptionErrorModel
    {
        public string Error { get; set; } = "";
        public string ErrorCode { get; set; } = "";
        public string ErrorVariable { get; set; }= "";
    }
}
