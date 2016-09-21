using I8Beef.Denon;
using System.Configuration;
using Topshelf;

namespace HomeAutio.Mqtt.Denon
{
    class Program
    {
        static void Main(string[] args)
        {
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

                x.Service<DenonMqttService>(s =>
                {
                    s.ConstructUsing(name => new DenonMqttService(denonClient, denonName, brokerIp, brokerPort, brokerUsername, brokerPassword));
                    s.WhenStarted(tc => tc.Start());
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
