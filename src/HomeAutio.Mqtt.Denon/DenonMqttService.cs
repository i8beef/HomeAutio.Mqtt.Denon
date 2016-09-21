using HomeAutio.Mqtt.Core;
using I8Beef.Denon;
using I8Beef.Denon.Commands;
using NLog;
using System.Collections.Generic;
using System.Text;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace HomeAutio.Mqtt.Denon
{
    public class DenonMqttService : ServiceBase
    {
        private ILogger _log = LogManager.GetCurrentClassLogger();

        private IClient _client;
        private string _denonName;

        public DenonMqttService(IClient denonClient, string denonName, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(brokerIp, brokerPort, brokerUsername, brokerPassword, "denon/" + denonName)
        {
            _subscribedTopics = new List<string>();
            _subscribedTopics.Add(_topicRoot + "/controls/+/set");

            _client = denonClient;
            _denonName = denonName;

            _client.EventReceived += Denon_EventReceived;

            // Denon client logging
            _client.MessageSent += (object sender, I8Beef.Denon.Events.MessageSentEventArgs e) => { _log.Debug("Denon Message sent: " + e.Message); };
            _client.MessageReceived += (object sender, I8Beef.Denon.Events.MessageReceivedEventArgs e) => { _log.Debug("Denon Message received: " + e.Message); };
            _client.Error += (object sender, System.IO.ErrorEventArgs e) => {
                _log.Error(e.GetException());

                // Stream parsing is lost at this point, restart the client (need to test this)
                _client.Close();
                _client.Connect();
            };
        }

        #region Service implementation

        /// <summary>
        /// Service Start action.
        /// </summary>
        public override void StartService()
        {
            _client.Connect();
        }

        /// <summary>
        /// Service Stop action.
        /// </summary>
        public override void StopService()
        {
            _client.Close();
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Harmony published to MQTT.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void Mqtt_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.Message);
            _log.Debug("MQTT message received for topic " + e.Topic + ": " + message);

            var commandType = e.Topic.Replace(_topicRoot + "/controls/", "").Replace("/set", "");

            Command command = null;
            if (commandType == "power")
                command = new PowerCommand { Value = message };

            if (commandType == "volume")
                command = new VolumeCommand { Value = message };

            if (commandType == "mute")
                command = new MuteCommand { Value = message };

            if (commandType == "input")
                command = new InputCommand { Value = message };

            if (commandType == "surroundMode")
                command = new SurroundModeCommand { Value = message };

            if (commandType == "tunerFrequency")
                command = new TunerFrequencyCommand { Value = message };

            if (commandType == "tunerMode")
                command = new TunerModeCommand { Value = message };

            if (commandType.StartsWith("zone"))
            {
                int zoneId;
                if (int.TryParse(commandType.Substring(4, 1), out zoneId))
                {
                    if (commandType == $"zone{zoneId}Power")
                        command = new ZonePowerCommand { Value = message, ZoneId = zoneId };

                    if (commandType == $"zone{zoneId}Volume")
                        command = new ZoneVolumeCommand { Value = message, ZoneId = zoneId };

                    if (commandType == $"zone{zoneId}Mute")
                        command = new ZoneMuteCommand { Value = message, ZoneId = zoneId };

                    if (commandType == $"zone{zoneId}Input")
                        command = new ZoneInputCommand { Value = message, ZoneId = zoneId };
                }
            }

            if (command != null)
                _client.SendCommandAsync(command);
        }

        #endregion

        #region Denon implementation

        /// <summary>
        /// Handles publishing updates to the harmony current activity to MQTT.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Denon_EventReceived(object sender, Command command)
        {
            _log.Debug($"Denon event received: {command.GetType()} {command.Code} {command.Value}");
            _mqttClient.Publish(_topicRoot + "/controls/" + command.GetType(), Encoding.UTF8.GetBytes(command.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
        }

        #endregion
    }
}
