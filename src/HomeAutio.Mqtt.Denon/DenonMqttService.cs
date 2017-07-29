using System;
using System.Text;
using HomeAutio.Mqtt.Core;
using I8Beef.Denon;
using I8Beef.Denon.Commands;
using I8Beef.Denon.Events;
using NLog;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace HomeAutio.Mqtt.Denon
{
    /// <summary>
    /// Denon MQTT Service.
    /// </summary>
    public class DenonMqttService : ServiceBase
    {
        private ILogger _log = LogManager.GetCurrentClassLogger();
        private bool _disposed = false;

        private IClient _client;
        private string _denonName;

        /// <summary>
        /// Initializes a new instance of the <see cref="DenonMqttService"/> class.
        /// </summary>
        /// <param name="denonClient">Denon client.</param>
        /// <param name="denonName">Denon name.</param>
        /// <param name="brokerIp">MQTT broker IP.</param>
        /// <param name="brokerPort">MQTT broker port.</param>
        /// <param name="brokerUsername">MQTT broker username.</param>
        /// <param name="brokerPassword">MQTT broker password.</param>
        public DenonMqttService(IClient denonClient, string denonName, string brokerIp, int brokerPort = 1883, string brokerUsername = null, string brokerPassword = null)
            : base(brokerIp, brokerPort, brokerUsername, brokerPassword, "denon/" + denonName)
        {
            SubscribedTopics.Add(TopicRoot + "/controls/+/set");

            _client = denonClient;
            _denonName = denonName;

            _client.EventReceived += Denon_EventReceived;

            // Denon client logging
            _client.MessageSent += (object sender, MessageSentEventArgs e) => { _log.Debug("Denon Message sent: " + e.Message); };
            _client.MessageReceived += (object sender, MessageReceivedEventArgs e) => { _log.Debug("Denon Message received: " + e.Message); };
            _client.Error += (object sender, System.IO.ErrorEventArgs e) =>
            {
                _log.Error(e.GetException());
                throw new Exception("Denon connection lost");
            };
        }

        #region Service implementation

        /// <summary>
        /// Service Start action.
        /// </summary>
        protected override void StartService()
        {
            _client.Connect();
            GetConfig();
        }

        /// <summary>
        /// Service Stop action.
        /// </summary>
        protected override void StopService()
        {
            Dispose();
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Harmony published to MQTT.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        protected override void Mqtt_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            var message = Encoding.UTF8.GetString(e.Message);
            _log.Debug("MQTT message received for topic " + e.Topic + ": " + message);

            var commandType = e.Topic.Replace(TopicRoot + "/controls/", string.Empty).Replace("/set", string.Empty);

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
                if (int.TryParse(commandType.Substring(4, 1), out int zoneId))
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
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Denon_EventReceived(object sender, CommandEventArgs e)
        {
            _log.Debug($"Denon event received: {e.Command.GetType()} {e.Command.Code} {e.Command.Value}");

            string commandType = null;
            switch (e.Command.GetType().Name)
            {
                case "PowerCommand":
                    commandType = "power";
                    break;
                case "VolumeCommand":
                    commandType = "volume";
                    break;
                case "MuteCommand":
                    commandType = "mute";
                    break;
                case "InputCommand":
                    commandType = "input";
                    break;
                case "SurroundModeCommand":
                    commandType = "surroundMode";
                    break;
                case "TunerFrequencyCommand":
                    commandType = "tunerFrequency";
                    break;
                case "TunerModeCommand":
                    commandType = "tunerMode";
                    break;
                case "ZonePowerCommand":
                    commandType = $"zone{((ZonePowerCommand)e.Command).ZoneId}Power";
                    break;
                case "ZoneVolumeCommand":
                    commandType = $"zone{((ZoneVolumeCommand)e.Command).ZoneId}Volume";
                    break;
                case "ZoneMuteCommand":
                    commandType = $"zone{((ZoneMuteCommand)e.Command).ZoneId}Mute";
                    break;
                case "ZoneInputCommand":
                    commandType = $"zone{((ZoneInputCommand)e.Command).ZoneId}Input";
                    break;
            }

            if (commandType != null)
                MqttClient.Publish(TopicRoot + "/controls/" + commandType, Encoding.UTF8.GetBytes(e.Command.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
        }

        /// <summary>
        /// Maps Denon device actions to subscription topics.
        /// </summary>
        private void GetConfig()
        {
            var commands = new Command[]
            {
                new PowerCommand { Value = "?" },
                new VolumeCommand { Value = "?" },
                new MuteCommand { Value = "?" },
                new InputCommand { Value = "?" },
                new SurroundModeCommand { Value = "?" },
                new TunerFrequencyCommand { Value = "?" },
                new TunerModeCommand { Value = "?" },
                new ZonePowerCommand { Value = "?", ZoneId = 2 },
                new ZoneVolumeCommand { Value = "?", ZoneId = 2 },
                new ZoneMuteCommand { Value = "?", ZoneId = 2 },
                new ZoneInputCommand { Value = "?", ZoneId = 2 }
            };

            // Run all queries and let the event handler publish out the query results
            foreach (var command in commands)
            {
                _client.SendCommandAsync(command);
            }
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Dispose implementation.
        /// </summary>
        /// <param name="disposing">Indicates if disposing.</param>
        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (_client != null)
                {
                    _client.Dispose();
                }
            }

            _disposed = true;
            base.Dispose(disposing);
        }

        #endregion
    }
}
