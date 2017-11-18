// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.PlatformAbstractions;

namespace Microsoft.DotNet.Cli.Telemetry
{
    public class Telemetry : ITelemetry
    {
        internal static string CurrentSessionId = null;
        private TelemetryClient _client = null;
        private Dictionary<string, string> _commonProperties = null;
        private Dictionary<string, double> _commonMeasurements = null;
        private Task _trackEventTask = null;

        private const string InstrumentationKey = "74cc1c9e-3e6e-4d05-b3fc-dde9101d0254";
        private const string TelemetryOptout = "DOTNET_CLI_TELEMETRY_OPTOUT";

        public bool Enabled { get; }

        public Telemetry() : this(null) { }

        public Telemetry(IFirstTimeUseNoticeSentinel sentinel) : this(sentinel, null) { }

        public Telemetry(IFirstTimeUseNoticeSentinel sentinel, string sessionId, bool blockThreadInitialization = false)
        {
            Enabled = !Env.GetEnvironmentVariableAsBool(TelemetryOptout) && PermissionExists(sentinel);

            if (!Enabled)
            {
                return;
            }

            // Store the session ID in a static field so that it can be reused
            CurrentSessionId = sessionId ?? Guid.NewGuid().ToString();

            if (blockThreadInitialization)
            {
                InitializeTelemetry();
            }
            else
            {
                //initialize in task to offload to parallel thread
                _trackEventTask = Task.Factory.StartNew(() => InitializeTelemetry());
            }
        }

        private bool PermissionExists(IFirstTimeUseNoticeSentinel sentinel)
        {
            if (sentinel == null)
            {
                return false;
            }

            return sentinel.Exists();
        }

        public void TrackEvent(string eventName, IDictionary<string, string> properties,
            IDictionary<string, double> measurements)
        {
            if (!Enabled)
            {
                return;
            }

            //continue task in existing parallel thread
            _trackEventTask = _trackEventTask.ContinueWith(
                x => TrackEventTask(eventName, properties, measurements)
            );
        }

        public void ThreadBlockingTrackEvent(string eventName, IDictionary<string, string> properties, IDictionary<string, double> measurements)
        {
            if (!Enabled)
            {
                return;
            }
            TrackEventTask(eventName, properties, measurements);
        }

        private void InitializeTelemetry()
        {
            try
            {
                _client = new TelemetryClient();
                _client.InstrumentationKey = InstrumentationKey;
                _client.Context.Session.Id = CurrentSessionId;
                _client.Context.Device.OperatingSystem = RuntimeEnvironment.OperatingSystem;

                _commonProperties = new TelemetryCommonProperties().GetTelemetryCommonProperties();
                _commonMeasurements = new Dictionary<string, double>();
            }
            catch (Exception e)
            {
                _client = null;
                // we dont want to fail the tool if telemetry fails.
                Debug.Fail(e.ToString());
            }
        }

        private void TrackEventTask(
            string eventName,
            IDictionary<string, string> properties,
            IDictionary<string, double> measurements)
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                Dictionary<string, string> eventProperties = GetEventProperties(properties);
                Dictionary<string, double> eventMeasurements = GetEventMeasures(measurements);

                _client.TrackEvent(PrependProducerNamespace(eventName), eventProperties, eventMeasurements);
                _client.Flush();
            }
            catch (Exception e)
            {
                Debug.Fail(e.ToString());
            }
        }

        private static string PrependProducerNamespace(string eventName)
        {
            return "dotnet/cli/" + eventName;
        }

        private Dictionary<string, double> GetEventMeasures(IDictionary<string, double> measurements)
        {
            Dictionary<string, double> eventMeasurements = new Dictionary<string, double>(_commonMeasurements);
            if (measurements != null)
            {
                foreach (KeyValuePair<string, double> measurement in measurements)
                {
                    if (eventMeasurements.ContainsKey(measurement.Key))
                    {
                        eventMeasurements[measurement.Key] = measurement.Value;
                    }
                    else
                    {
                        eventMeasurements.Add(measurement.Key, measurement.Value);
                    }
                }
            }
            return eventMeasurements;
        }

        private Dictionary<string, string> GetEventProperties(IDictionary<string, string> properties)
        {
            if (properties != null)
            {
                var eventProperties = new Dictionary<string, string>(_commonProperties);
                foreach (KeyValuePair<string, string> property in properties)
                {
                    if (eventProperties.ContainsKey(property.Key))
                    {
                        eventProperties[property.Key] = property.Value;
                    }
                    else
                    {
                        eventProperties.Add(property.Key, property.Value);
                    }
                }
                return eventProperties;
            }
            else
            {
                return _commonProperties;
            }
        }
    }
}
