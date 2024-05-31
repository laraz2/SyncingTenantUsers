namespace SyncingTenantUsers.Models.ErrorModels
{
    public class AppExceptionErrorModel
    {

        public string message { get; set; }

        public ErrorObject error { get; set; }

    }

    public class ErrorObject
    {
        public string error { get; set; }
        public string errorCode { get; set; }
    }
}
