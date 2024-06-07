using SyncingTenantUsers.Models.ErrorModels;
using Microsoft.Extensions.Configuration;
using System;
using System.Net;

namespace SyncingTenantUsers.Helpers
{
    public class AppException : Exception
    {
        public AppExceptionErrorModel AppExceptionErrorModel { get; set; }
        public ErrorObject ErrorObject { get; set; }
        public int StatusCode { get; set; }
        public AppException(AppExceptionErrorModel AppExceptionErrorModel, ErrorObject errorObject, int statusCode)
        {
            this.ErrorObject = errorObject;
            this.AppExceptionErrorModel = AppExceptionErrorModel;
            this.StatusCode =   statusCode;
        }


        public AppException(string message, HttpStatusCode statusCode) 
        {
            AppExceptionErrorModel = new AppExceptionErrorModel
            {
                message =  message,
                error = new ErrorObject
                {
                    error = message,
                    errorCode = message,

                }
            };
            StatusCode = (int)(statusCode);
        }


        public AppException(string message, string _secondMessage)
        {

            AppExceptionErrorModel = new AppExceptionErrorModel
            {
                message = _secondMessage,
                error = new ErrorObject
                {
                    error = message,
                    errorCode = message,

                }
            };

        }

        public AppException(string message, string _secondMessage, string functinoName, string model)
        {

            AppExceptionErrorModel = new AppExceptionErrorModel
            {
                message = _secondMessage,
                error = new ErrorObject
                {
                    error = message,
                    errorCode = message,

                }
            };

        }

        public AppException(string message, string _secondMessage, HttpStatusCode statusCode)
        {

            AppExceptionErrorModel = new AppExceptionErrorModel
            {
                message = _secondMessage,
                error = new ErrorObject
                {
                    error = message,
                    errorCode = message,

                }
            };
            StatusCode = (int)(statusCode);


        }



    }
}
