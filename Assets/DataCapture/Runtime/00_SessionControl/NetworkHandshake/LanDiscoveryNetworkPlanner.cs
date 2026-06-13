using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DataCapture.Networking
{
    internal static class LanDiscoveryNetworkPlanner
    {
        private static readonly string[] TunnelNameHints =
        {
            "tun", "tap", "vpn", "wg", "wireguard", "zerotier", "radmin", "vmware", "veth", "tailscale"
        };

        public static Plan Build(NetworkSenderConfigurationSO configuration, bool isPlaying)
        {
            var plan = new Plan
            {
                BindAddress = ResolveBindAddress(configuration, isPlaying)
            };

            if (configuration == null)
            {
                plan.Warning = "Network sender configuration is missing.";
                return plan;
            }

            if (configuration.includeLimitedBroadcast)
            {
                AddTarget(plan, IPAddress.Broadcast, configuration.discoveryPort, "limited-broadcast");
            }

            if (!isPlaying &&
                !string.IsNullOrWhiteSpace(configuration.editorDiscoveryBroadcastAddress) &&
                IPAddress.TryParse(configuration.editorDiscoveryBroadcastAddress, out IPAddress editorBroadcastAddress))
            {
                AddTarget(plan, editorBroadcastAddress, configuration.discoveryPort, "editor-override");
            }

            try
            {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    AddInterfaceTargets(plan, configuration, networkInterface);
                }
            }
            catch (Exception ex) when (ex is NetworkInformationException || ex is SocketException || ex is NotSupportedException)
            {
                plan.Warning = Append(plan.Warning, "Could not enumerate local network interfaces: " + ex.Message);
            }

            AddConfiguredTargets(plan, configuration);
            plan.DeduplicateTargets();
            plan.BuildSummaries();
            return plan;
        }

        private static void AddInterfaceTargets(
            Plan plan,
            NetworkSenderConfigurationSO configuration,
            NetworkInterface networkInterface)
        {
            if (networkInterface == null || networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                return;
            }

            bool tunnelLike = IsTunnelLike(networkInterface);
            if (tunnelLike)
            {
                plan.VpnOrTunnelInterfaceDetected = true;
            }

            if (tunnelLike && configuration.avoidVpnTunnelInterfaces)
            {
                plan.IgnoredInterfaceCount++;
                plan.AddInterfaceSummary(networkInterface.Name, networkInterface.NetworkInterfaceType, "ignored-vpn");
                return;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                return;
            }

            bool addedInterface = false;
            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address == null ||
                    unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(unicast.Address))
                {
                    continue;
                }

                addedInterface = true;

                if (!configuration.includeDirectedBroadcasts)
                {
                    continue;
                }

                IPAddress broadcast = TryGetDirectedBroadcast(unicast.Address, unicast.IPv4Mask);
                if (broadcast != null)
                {
                    AddTarget(
                        plan,
                        broadcast,
                        configuration.discoveryPort,
                        networkInterface.Name + "/" + unicast.Address);
                }
            }

            if (addedInterface)
            {
                plan.CandidateInterfaceCount++;
                plan.AddInterfaceSummary(networkInterface.Name, networkInterface.NetworkInterfaceType, "candidate");
            }
        }

        private static void AddConfiguredTargets(Plan plan, NetworkSenderConfigurationSO configuration)
        {
            if (configuration.additionalDiscoveryTargets == null)
            {
                return;
            }

            foreach (string rawTarget in configuration.additionalDiscoveryTargets)
            {
                if (string.IsNullOrWhiteSpace(rawTarget))
                {
                    continue;
                }

                if (TryParseTarget(rawTarget.Trim(), configuration.discoveryPort, out IPEndPoint target))
                {
                    plan.Targets.Add(new DiscoveryTarget(target, "configured"));
                }
                else
                {
                    plan.Warning = Append(plan.Warning, "Ignored invalid configured discovery target: " + rawTarget);
                }
            }
        }

        private static bool TryParseTarget(string rawTarget, int defaultPort, out IPEndPoint target)
        {
            target = null;
            string host = rawTarget;
            int port = defaultPort;

            int colonIndex = rawTarget.LastIndexOf(':');
            if (colonIndex > 0 && colonIndex < rawTarget.Length - 1)
            {
                host = rawTarget.Substring(0, colonIndex);
                if (!int.TryParse(rawTarget.Substring(colonIndex + 1), out port))
                {
                    return false;
                }
            }

            if (!IPAddress.TryParse(host, out IPAddress address))
            {
                return false;
            }

            port = Mathf.Clamp(port, 1, 65535);
            target = new IPEndPoint(address, port);
            return true;
        }

        private static void AddTarget(Plan plan, IPAddress address, int port, string source)
        {
            if (address == null)
            {
                return;
            }

            plan.Targets.Add(new DiscoveryTarget(new IPEndPoint(address, port), source));
        }

        private static IPAddress ResolveBindAddress(NetworkSenderConfigurationSO configuration, bool isPlaying)
        {
            if (!isPlaying &&
                configuration != null &&
                !string.IsNullOrWhiteSpace(configuration.editorLocalBindAddress) &&
                IPAddress.TryParse(configuration.editorLocalBindAddress, out IPAddress editorBindAddress))
            {
                return editorBindAddress;
            }

            return IPAddress.Any;
        }

        private static IPAddress TryGetDirectedBroadcast(IPAddress address, IPAddress mask)
        {
            if (address == null || mask == null)
            {
                return null;
            }

            byte[] addressBytes = address.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            if (addressBytes.Length != 4 || maskBytes.Length != 4)
            {
                return null;
            }

            byte[] broadcastBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
            }

            return new IPAddress(broadcastBytes);
        }

        private static bool IsTunnelLike(NetworkInterface networkInterface)
        {
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ppp)
            {
                return true;
            }

            string name = (networkInterface.Name ?? string.Empty).ToLowerInvariant();
            string description = (networkInterface.Description ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < TunnelNameHints.Length; i++)
            {
                string hint = TunnelNameHints[i];
                if (name.Contains(hint) || description.Contains(hint))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Append(string existing, string message)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return message;
            }

            return existing + " " + message;
        }

        internal sealed class Plan
        {
            public IPAddress BindAddress;
            public readonly List<DiscoveryTarget> Targets = new List<DiscoveryTarget>();
            public bool VpnOrTunnelInterfaceDetected;
            public int CandidateInterfaceCount;
            public int IgnoredInterfaceCount;
            public string Warning = string.Empty;
            public string LocalNetworkSummary = string.Empty;
            public string TargetSummary = string.Empty;

            private readonly List<string> interfaceSummaries = new List<string>();

            public void AddInterfaceSummary(string name, NetworkInterfaceType type, string state)
            {
                interfaceSummaries.Add(name + "(" + type + "," + state + ")");
            }

            public void DeduplicateTargets()
            {
                var seen = new HashSet<string>();
                for (int i = Targets.Count - 1; i >= 0; i--)
                {
                    string key = Targets[i].EndPoint.Address + ":" + Targets[i].EndPoint.Port;
                    if (!seen.Add(key))
                    {
                        Targets.RemoveAt(i);
                    }
                }
            }

            public void BuildSummaries()
            {
                if (VpnOrTunnelInterfaceDetected)
                {
                    Warning = Append(
                        Warning,
                        "VPN/tunnel-like network interface detected. Disable headset/PC VPN if LAN discovery or direct HTTP fails.");
                }

                LocalNetworkSummary = interfaceSummaries.Count == 0
                    ? "No active IPv4 LAN interface candidates."
                    : string.Join("; ", interfaceSummaries);

                if (Targets.Count == 0)
                {
                    TargetSummary = "No discovery targets.";
                    return;
                }

                var builder = new StringBuilder();
                for (int i = 0; i < Targets.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(Targets[i].EndPoint.Address);
                    builder.Append(':');
                    builder.Append(Targets[i].EndPoint.Port);
                    builder.Append('(');
                    builder.Append(Targets[i].Source);
                    builder.Append(')');
                }

                TargetSummary = builder.ToString();
            }
        }

        internal struct DiscoveryTarget
        {
            public readonly IPEndPoint EndPoint;
            public readonly string Source;

            public DiscoveryTarget(IPEndPoint endPoint, string source)
            {
                EndPoint = endPoint;
                Source = source;
            }
        }
    }
}
