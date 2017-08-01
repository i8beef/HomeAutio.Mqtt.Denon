using System.Configuration;
using I8Beef.Denon;
using NLog;
using Topshelf;

namespace HomeAutio.Mqtt.Denon
{
    /// <summary>
    /// Main program entrypoint.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main method.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static void Main(string[] args)
        {
            var log = LogManager.GetCurrentClassLogger();

            var brokerIp = ConfigurationManager.AppSettings["brokerIp"];
            var brokerPort = int.Parse(ConfigurationManager.AppSettings["brokerPort"]);
            var brokerUsername = ConfigurationManager.AppSettings["brokerUsername"];
            var brokerPassword = ConfigurationManager.AppSettings["brokerPassword"];

            var denonIp = ConfigurationManager.AppSettings["denonIp"];
            IClient denonClient;
            if (ConfigurationManager.AppSettings["denonType"] == "telnet")
                denonClient = new I8Beef.Denon.TelnetClient.Client(denonIp);
            else
                denonClient = new I8Beef.Denon.HttpClient.Client(denonIp);

            var denonName = ConfigurationManager.AppSettings["denonName"];

            HostFactory.Run(x =>
            {
                x.UseNLog();
                x.OnException(ex => log.Error(ex));

                x.Service<DenonMqttService>(s =>
                {
                    s.ConstructUsing(name => new DenonMqttService(denonClient, denonName, brokerIp, brokerPort, brokerUsername, brokerPassword));
                    s.WhenStarted((tc, hostControl) => tc.Start(hostControl));
                    s.WhenStopped(tc => tc.Stop());
                });

                x.EnableServiceRecovery(r =>
                {
                    r.RestartService(0);
                    r.RestartService(0);
                    r.RestartService(0);
                });

                x.RunAsLocalSystem();
                x.UseAssemblyInfoForServiceInfo();
            });
        }
    }
}
