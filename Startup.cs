using SyncingTenantUsers.Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

using System.Threading;

[assembly: FunctionsStartup(typeof(SyncingTenantUsers.Startup))]

namespace SyncingTenantUsers
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            // Add services to the container.
            builder.Services.AddMvcCore();

            var services = builder.Services;

            services.AddHttpContextAccessor();
            services.AddScoped<IAccountServices, AccountServices>();

            // Add ExecutionContext
            services.AddSingleton<ExecutionContext>();
            services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder => builder.WithOrigins("https://entelligence365.crm4.dynamics.com/")
                                      .AllowAnyHeader()
                                      .AllowAnyMethod());
            });


        }
    }
}


//for azure fct and local host
//using WordPressFormRegistration.Services;
//using Microsoft.Azure.Functions.Extensions.DependencyInjection;
//using Microsoft.Extensions.DependencyInjection;

//[assembly: FunctionsStartup(typeof(WordPressFormRegistration.Startup))]

//namespace WordPressFormRegistration
//{
//    public class Startup : FunctionsStartup
//    {
//        public override void Configure(IFunctionsHostBuilder builder)
//        {
//            // Add services to the container.
//            builder.Services.AddMvcCore();

//            var services = builder.Services;

//            services.AddHttpContextAccessor();
//            services.AddScoped<ILeadsServices, LeadsServices>();

//            //services.AddCors(options =>
//            //{
//            //    options.AddPolicy("AllowSpecificOrigin", builder =>
//            //    {
//            //        builder.WithOrigins("https://www.activ365.cloud")
//            //               .AllowAnyHeader()
//            //               .AllowAnyMethod();
//            //    });
//            //});

//        }
//    }
//}