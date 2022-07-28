// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TwitApp.Data;
using TwitApp.HostedServices;
using TwitApp.Services;
using Tweetinvi;
using System.Configuration;

CreateHostBuilder(args).Build().Run();

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureServices((hostContext, services) =>
        {
            //services.AddLogging();
            services.AddTransient<ITwitService, TwitService>();
            services.AddDbContext<TwitContext>();
            services.AddHostedService<HostedService>();
           
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json").Build();

            var tweetinvi = new TwitterClient(config["ConsumerKey"], config["ConsumerSecret"], config["AccessToken"], config["AccessSecret"]);
            tweetinvi.Config.RateLimitTrackerMode = RateLimitTrackerMode.TrackAndAwait;
            
            services.AddSingleton(tweetinvi);
        })
        .ConfigureAppConfiguration((hostingContext, config) =>
        {
            //var env = hostingContext.HostingEnvironment;

            config.AddEnvironmentVariables();
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        });