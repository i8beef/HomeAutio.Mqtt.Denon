using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using I8Beef.Denon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HomeAutio.Mqtt.Denon
{
    /// <summary>
    /// Main program entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args">Arguments.</param>
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Main program entry point.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Awaitable <see cref="Task" />.</returns>
        public static async Task MainAsync(string[] args)
        {
            var environmentName = Environment.GetEnvironmentVariable("ENVIRONMENT");
            if (string.IsNullOrEmpty(environmentName))
                environmentName = "Development";

            // Setup config
            var config = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
                .Build();

            // Setup logging
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();

            try
            {
                var hostBuilder = CreateHostBuilder(config);
                await hostBuilder.RunConsoleAsync();
            }
            catch (Exception ex)
            {
                Log.Logger.Fatal(ex, ex.Message);
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Creates an <see cref="IHostBuilder"/>.
        /// </summary>
        /// <param name="config">External configuration.</param>
        /// <returns>A configured <see cref="IHostBuilder"/>.</returns>
        private static IHostBuilder CreateHostBuilder(IConfiguration config)
        {
            return new HostBuilder()
                .ConfigureAppConfiguration((hostContext, configuration) => configuration.AddConfiguration(config))
                .ConfigureLogging((hostingContext, logging) => logging.AddSerilog())
                .ConfigureServices((hostContext, services) =>
                {
                    // Setup client
                    services.AddScoped<IClient>(serviceProvider =>
                    {
                        if (config.GetValue<string>("denon:denonConnectionType") == "http")
                        {
                            return new I8Beef.Denon.HttpClient.Client(config.GetValue<string>("denon:denonHost"));
                        }
                        else
                        {
                            return new I8Beef.Denon.TelnetClient.Client(config.GetValue<string>("denon:denonHost"));
                        }
                    });

                    // Setup service instance
                    services.AddScoped<IHostedService, DenonMqttService>(serviceProvider =>
                    {
                        var brokerSettings = new Core.BrokerSettings
                        {
                            BrokerIp = config.GetValue<string>("mqtt:brokerIp"),
                            BrokerPort = config.GetValue<int>("mqtt:brokerPort"),
                            BrokerUsername = config.GetValue<string>("mqtt:brokerUsername"),
                            BrokerPassword = config.GetValue<string>("mqtt:brokerPassword"),
                            BrokerUseTls = config.GetValue<bool>("mqtt:brokerUseTls", false)
                        };

                        // TLS settings
                        if (brokerSettings.BrokerUseTls)
                        {
                            var brokerTlsSettings = new Core.BrokerTlsSettings
                            {
                                AllowUntrustedCertificates = config.GetValue<bool>("mqtt:brokerTlsSettings:allowUntrustedCertificates", false),
                                IgnoreCertificateChainErrors = config.GetValue<bool>("mqtt:brokerTlsSettings:ignoreCertificateChainErrors", false),
                                IgnoreCertificateRevocationErrors = config.GetValue<bool>("mqtt:brokerTlsSettings:ignoreCertificateRevocationErrors", false)
                            };

                            switch (config.GetValue<string>("mqtt:brokerTlsSettings:protocol", "1.2"))
                            {
                                case "1.0":
                                    brokerTlsSettings.SslProtocol = System.Security.Authentication.SslProtocols.Tls;
                                    break;
                                case "1.1":
                                    brokerTlsSettings.SslProtocol = System.Security.Authentication.SslProtocols.Tls11;
                                    break;
                                case "1.2":
                                default:
                                    brokerTlsSettings.SslProtocol = System.Security.Authentication.SslProtocols.Tls12;
                                    break;
                            }

                            var brokerTlsCertificatesSection = config.GetSection("mqtt:brokerTlsSettings:certificates");
                            brokerTlsSettings.Certificates = brokerTlsCertificatesSection.GetChildren()
                                .Select(x =>
                                {
                                    var file = x.GetValue<string>("file");
                                    var passPhrase = x.GetValue<string>("passPhrase");

                                    if (!File.Exists(file))
                                        throw new FileNotFoundException($"Broker Certificate '{file}' is missing!");

                                    return !string.IsNullOrEmpty(passPhrase) ?
                                        new X509Certificate2(file, passPhrase) :
                                        new X509Certificate2(file);
                                }).ToList();

                            brokerSettings.BrokerTlsSettings = brokerTlsSettings;
                        }

                        return new DenonMqttService(
                            serviceProvider.GetRequiredService<ILogger<DenonMqttService>>(),
                            serviceProvider.GetRequiredService<IClient>(),
                            config.GetValue<string>("denon:denonName"),
                            brokerSettings);
                    });
                });
        }
    }
}
