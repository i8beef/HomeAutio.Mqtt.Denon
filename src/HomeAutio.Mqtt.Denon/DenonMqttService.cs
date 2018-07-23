using System.Threading;
using System.Threading.Tasks;
using HomeAutio.Mqtt.Core;
using I8Beef.Denon;
using I8Beef.Denon.Commands;
using I8Beef.Denon.Events;
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace HomeAutio.Mqtt.Denon
{
    /// <summary>
    /// Denon MQTT Service.
    /// </summary>
    public class DenonMqttService : ServiceBase
    {
        private ILogger<DenonMqttService> _log;
        private bool _disposed = false;

        private IClient _client;
        private string _denonName;

        /// <summary>
        /// Initializes a new instance of the <see cref="DenonMqttService"/> class.
        /// </summary>
        /// <param name="logger">Logging instance.</param>
        /// <param name="denonClient">Denon client.</param>
        /// <param name="denonName">Denon name.</param>
        /// <param name="brokerSettings">MQTT broker settings.</param>
        public DenonMqttService(
            ILogger<DenonMqttService> logger,
            IClient denonClient,
            string denonName,
            BrokerSettings brokerSettings)
            : base(logger, brokerSettings, "denon/" + denonName)
        {
            _log = logger;
            SubscribedTopics.Add(TopicRoot + "/controls/+/set");

            _client = denonClient;
            _denonName = denonName;

            _client.EventReceived += Denon_EventReceived;

            // Denon client logging
            _client.MessageSent += (object sender, MessageSentEventArgs e) => { _log.LogInformation("Denon Message sent: " + e.Message); };
            _client.MessageReceived += (object sender, MessageReceivedEventArgs e) => { _log.LogInformation("Denon Message received: " + e.Message); };
            _client.Error += (object sender, System.IO.ErrorEventArgs e) =>
            {
                var exception = e.GetException();
                _log.LogError(exception, exception.Message);
                System.Environment.Exit(1);
            };
        }

        #region Service implementation

        /// <inheritdoc />
        protected override async Task StartServiceAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            _client.Connect();
            await GetConfigAsync()
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override Task StopServiceAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.CompletedTask;
        }

        #endregion

        #region MQTT Implementation

        /// <summary>
        /// Handles commands for the Denon published to MQTT.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        protected override async void Mqtt_MqttMsgPublishReceived(object sender, MqttApplicationMessageReceivedEventArgs e)
        {
            var message = e.ApplicationMessage.ConvertPayloadToString();
            _log.LogInformation("MQTT message received for topic " + e.ApplicationMessage.Topic + ": " + message);

            var commandType = e.ApplicationMessage.Topic.Replace(TopicRoot + "/controls/", string.Empty).Replace("/set", string.Empty);

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
            {
                await _client.SendCommandAsync(command)
                    .ConfigureAwait(false);
            }
        }

        #endregion

        #region Denon implementation

        /// <summary>
        /// Handles publishing updates to the Denon current activity to MQTT.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void Denon_EventReceived(object sender, CommandEventArgs e)
        {
            _log.LogInformation($"Denon event received: {e.Command.GetType()} {e.Command.Code} {e.Command.Value}");

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
            {
                await MqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                        .WithTopic(TopicRoot + "/controls/" + commandType)
                        .WithPayload(e.Command.Value)
                        .WithAtLeastOnceQoS()
                        .WithRetainFlag()
                        .Build())
                        .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Maps Denon device actions to subscription topics.
        /// </summary>
        /// <returns>An awaitable <see cref="Task"/>.</returns>
        private async Task GetConfigAsync()
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
                await _client.SendCommandAsync(command)
                    .ConfigureAwait(false);
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
