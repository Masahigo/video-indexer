using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoIndexerApi.Services;
using dotenv.net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Collections.Generic;
using VideoIndexerApi.HealthChecks;

namespace VideoIndexerApi
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers().AddDapr();
            services.AddHostedService<QueuedHostedService>();
            services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();

            services.AddHealthChecks()
                .AddLivenessHealthCheck("Liveness", HealthStatus.Unhealthy, new List<string>() { "Liveness" })
                .AddReadinessHealthCheck("Readiness", HealthStatus.Unhealthy, new List<string> { "Readiness" });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                DotEnv.Load();
                app.UseDeveloperExceptionPage();
            }

            // don't enforce https for simplicity
            //app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseCloudEvents();

            string route = Configuration.GetValue<string>("EVENTGRID_INPUT_ROUTE");
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapSubscribeHandler();
                endpoints.MapControllers();

                endpoints.MapHealthChecks("/healthz", new HealthCheckOptions()
                {
                    Predicate = check => check.Name == "Liveness"
                });

                endpoints.MapHealthChecks("/ready", new HealthCheckOptions()
                {
                    Predicate = check => check.Name == "Readiness"
                });

                endpoints.MapControllerRoute(name: "EventGridInput",
                            pattern: route,
                            defaults: new { controller = "VideoIndexer", action = "Post" });

            });
        }
    }
}
