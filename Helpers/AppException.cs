using Microsoft.Extensions.Configuration;
using System;
using SyncingTenantUsers.Models.ErrorModels;


namespace SyncingTenantUsers.Helpers
{


    public class AppException : Exception
    {
        private readonly IConfiguration _configuration;

        public AppExceptionErrorModel ErrorObject { get; set; }
        public AppException(IConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }
        public AppException(AppExceptionErrorModel errorObject, IConfiguration configuration)
        {
            ErrorObject = errorObject;
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public AppException(string message, IConfiguration configuration)
            : base(configuration.GetSection("BackendErrors")[message] ?? message)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            ErrorObject = new AppExceptionErrorModel
            {
                Error = configuration.GetSection("BackendErrors")[message] ?? message,
                ErrorCode = message,
            };
        }

        public AppException(string message, string secondMessage, IConfiguration configuration)
            : base(configuration.GetSection("BackendErrors")[message] ?? message)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            if (!string.IsNullOrEmpty(secondMessage))
            {
                string errorToLog = $"Log Written Date: {DateTime.UtcNow.ToString()} | Error Message: {secondMessage}\n----------------------------------------------------------\n";
               // LogErrorToAzure(errorToLog, _configuration);
            }

            var errorMessage = _configuration.GetSection("BackendErrors")?[message];
            ErrorObject = new AppExceptionErrorModel
            {
                Error = errorMessage ?? "",
                ErrorCode = message,
            };
        }

        // Other constructors follow the same pattern

        //public void LogErrorToAzure(string errorMessage, IConfiguration _configuration)
        //{
        //    string azureStorageConnectionString = _configuration.GetSection("Azure-Storage")["ConnectionString"]!;
        //    string containerName = _configuration.GetSection("Azure-Storage")["ContainerName"]!;
        //    string blobName = _configuration.GetSection("Azure-Storage")["BlobName"]!;

        //    BlobContainerClient containerClient = new BlobContainerClient(azureStorageConnectionString, containerName);

        //    // Ensure container exists
        //    containerClient.CreateIfNotExists();

        //    // Get blob reference
        //    BlobClient blobClient = containerClient.GetBlobClient(blobName);

        //    // Create or append to blob
        //    using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(errorMessage)))
        //    {
        //        blobClient.Upload(stream, true);
        //    }
        //}
    }
}
