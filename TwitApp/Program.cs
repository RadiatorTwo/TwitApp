// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TwitApp.Data;
using TwitApp.HostedServices;
using TwitApp.Services;
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

            var coreTweet = new CoreTweet.Tokens();
            coreTweet.ConsumerKey = config["ConsumerKey"];
            coreTweet.ConsumerSecret = config["ConsumerSecret"];
            coreTweet.AccessToken = config["AccessToken"];
            coreTweet.AccessTokenSecret = config["AccessSecret"];

            services.AddSingleton(coreTweet);
        })
        .ConfigureAppConfiguration((hostingContext, config) =>
        {
            //var env = hostingContext.HostingEnvironment;

            config.AddEnvironmentVariables();
            config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        });