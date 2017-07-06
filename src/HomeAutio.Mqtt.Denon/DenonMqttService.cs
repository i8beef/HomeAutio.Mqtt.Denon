using HomeAutio.Mqtt.Core;
using I8Beef.Denon;
using I8Beef.Denon.Commands;
using NLog;
using System;
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
                throw new System.Exception("Denon connection lost");
            };
        }

        #region Service implementation

        /// <summary>
        /// Service Start action.
        /// </summary>
        public override void StartService()
        {
            try
            {
                _client.Connect();
                GetConfig();
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                throw;
            }
        }

        /// <summary>
        /// Service Stop action.
        /// </summary>
        public override void StopService()
        {
            try
            {
                _client.Close();
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                throw;
            }
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
            try
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
            catch (Exception ex)
            {
                _log.Error(ex);
                throw;
            }
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
            try
            {
                _log.Debug($"Denon event received: {command.GetType()} {command.Code} {command.Value}");

                string commandType = null;
                switch (command.GetType().Name)
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
                        commandType = $"zone{((ZonePowerCommand)command).ZoneId}Power";
                        break;
                    case "ZoneVolumeCommand":
                        commandType = $"zone{((ZoneVolumeCommand)command).ZoneId}Volume";
                        break;
                    case "ZoneMuteCommand":
                        commandType = $"zone{((ZoneMuteCommand)command).ZoneId}Mute";
                        break;
                    case "ZoneInputCommand":
                        commandType = $"zone{((ZoneInputCommand)command).ZoneId}Input";
                        break;
                }

                if (commandType != null)
                    _mqttClient.Publish(_topicRoot + "/controls/" + commandType, Encoding.UTF8.GetBytes(command.Value), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                throw;
            }
        }

        /// <summary>
        /// Maps Denon device actions to subscription topics.
        /// </summary>
        private void GetConfig()
        {
            try
            {
                var commands = new Command[] {
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
            catch (Exception ex)
            {
                _log.Error(ex);
                throw;
            }
        }

        #endregion
    }
}
