// <copyright file="CaptivePortalDetection.cs" company="Mozilla">
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, you can obtain one at http://mozilla.org/MPL/2.0/.
// </copyright>

/* SPDX-License-Identifier: MIT
 *
 * Copyright (C) 2019 Edge Security LLC. All Rights Reserved.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAPICodePack.Net;

namespace FirefoxPrivateNetwork.Network
{
    /// <summary>
    /// Used for detecting connectivity issues due to captive portals.
    /// </summary>
    public class CaptivePortalDetection
    {
        private bool captivePortalDetected = false;
        private List<Microsoft.WindowsAPICodePack.Net.Network> connectedNetworks = new List<Microsoft.WindowsAPICodePack.Net.Network>();
        private CancellationTokenSource monitorInternetConnectivityTokenSource = new CancellationTokenSource();
        private TimeSpan postLoginNotificationGracePeriod = TimeSpan.FromSeconds(10);
        private TimeSpan monitorInternetConnectivityFrequency = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Initializes a new instance of the <see cref="CaptivePortalDetection"/> class.
        /// </summary>
        public CaptivePortalDetection()
        {
            // Add an event handler to reset the captive portal detection check when a network change is detected
            ConfigureNetworkAddressChangedHandler();
        }

        /// <summary>
        /// Connectivity status result when checking for connectivity/captive portal presense.
        /// </summary>
        public enum ConnectivityStatus
        {
            /// <summary>
            /// There is no internet connectivity available at this time.
            /// </summary>
            NoConnectivity = -1,

            /// <summary>
            /// Potential internet connectivity, as a captive portal has been detected.
            /// </summary>
            CaptivePortalDetected,

            /// <summary>
            /// There is internet connectivity available.
            /// </summary>
            HaveConnectivity,
        }

        /// <summary>
        /// Gets or sets a value indicating whether we have detected a captive portal network.
        /// </summary>
        public bool CaptivePortalDetected
        {
            get
            {
                return captivePortalDetected;
            }

            set
            {
                captivePortalDetected = value;
                if (value)
                {
                    CaptivePortalLoggedIn = false;

                    // Send a windows notification if captive portal is detected.
                    Manager.TrayIcon.ShowNotification(
                        Manager.TranslationService.GetString("windows-notification-captive-portal-title"),
                        Manager.TranslationService.GetString("windows-notification-captive-portal-content"),
                        NotificationArea.ToastIconType.Disconnected
                    );
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the user has logged in to the detected captive portal or not.
        /// </summary>
        public bool CaptivePortalLoggedIn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether internet connection is being monitored or not.
        /// </summary>
        public bool MonitoringInternetConnection { get; set; } = false;

        /// <summary>
        /// Adds a route to specified IP and tries to retrieve a URL from a host, then checks downloaded contents of file with expectedTestResult.
        /// This will check whether there is any connectivity outside of the confines of a potentially active WireGuard tunnel.
        /// </summary>
        /// <param name="ip">IP address to add to routing table (to route traffic outside of the tunnel).</param>
        /// <param name="host">Host to contact for captive portal detection.</param>
        /// <param name="url">URL to download.</param>
        /// <param name="expectedTestResult">Expected contents of the downloaded file (e.g. "success").</param>
        /// <returns>Returns connectivity status indicating current connection state.</returns>
        [DllImport("tunnel.dll", EntryPoint = "TestOutsideConnectivity", CallingConvention = CallingConvention.Cdecl)]
        public static extern ConnectivityStatus TestOutsideConnectivity([MarshalAs(UnmanagedType.LPWStr)] string ip, [MarshalAs(UnmanagedType.LPWStr)] string host, [MarshalAs(UnmanagedType.LPWStr)] string url, [MarshalAs(UnmanagedType.LPWStr)] string expectedTestResult);

        /// <summary>
        /// Checks whether we are located on a captive portal network.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation which returns a ConnectivityStatus value.</returns>
        public static Task<ConnectivityStatus> IsCaptivePortalActiveTask()
        {
            var testOutsideConnectivityTask = Task.Run(() =>
            {
                return TestOutsideConnectivity(ProductConstants.CaptivePortalDetectionIP, ProductConstants.CaptivePortalDetectionHost, ProductConstants.CaptivePortalDetectionUrl, ProductConstants.CaptivePortalDetectionValidReplyContents);
            });

            return testOutsideConnectivityTask;
        }

        /// <summary>
        /// Initiates a task that monitors the current captive portal network for internet connectivity.
        /// </summary>
        public void MonitorInternetConnectivity()
        {
            monitorInternetConnectivityTokenSource = new CancellationTokenSource();

            Task.Run(() =>
            {
                Debug.WriteLine("Start monitoring internet connectivity");
                MonitoringInternetConnection = true;

                while (!monitorInternetConnectivityTokenSource.Token.IsCancellationRequested)
                {
                    if (CheckInternetConnectivity())
                    {
                        monitorInternetConnectivityTokenSource.Token.WaitHandle.WaitOne(postLoginNotificationGracePeriod);

                        if (Manager.MainWindowViewModel.Status == Models.ConnectionState.Unprotected)
                        {
                            Manager.TrayIcon.ShowNotification(
                            "Guest Wi-Fi network detected",
                            "Turn on VPN to secure your device.",
                            NotificationArea.ToastIconType.Disconnected
                            );
                        }

                        CaptivePortalLoggedIn = true;
                        MonitoringInternetConnection = false;
                        CaptivePortalDetected = false;
                        Debug.WriteLine("Done monitoring internet connectivity.  Exiting task.");
                        return;
                    }

                    monitorInternetConnectivityTokenSource.Token.WaitHandle.WaitOne(monitorInternetConnectivityFrequency);
                }
            }, monitorInternetConnectivityTokenSource.Token);
        }

        /// <summary>
        /// Cancels the task that monitors the current captive portal network for internet connectivity.
        /// </summary>
        public void StopMonitorInternetConnectivity()
        {
            monitorInternetConnectivityTokenSource.Cancel();
            MonitoringInternetConnection = false;
            CaptivePortalDetected = false;
        }

        private bool CheckInternetConnectivity()
        {
            try
            {
                var uri = ProductConstants.CaptivePortalDetectionUrl.Replace("%s", ProductConstants.CaptivePortalDetectionHost);
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "HEAD";
                request.AllowAutoRedirect = false;

                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureNetworkAddressChangedHandler()
        {
            NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler((sender, e) =>
            {
                var networks = NetworkListManager.GetNetworks(NetworkConnectivityLevels.Connected).GetEnumerator();
                var newConnectedNetworks = new List<Microsoft.WindowsAPICodePack.Net.Network>();
                while (networks.MoveNext())
                {
                    if (networks.Current.Name != ProductConstants.InternalAppName)
                    {
                        newConnectedNetworks.Add(networks.Current);
                    }
                }

                if (newConnectedNetworks.Count > 0)
                {
                    var connectedNetworkIds = new HashSet<Guid>(connectedNetworks.Select(n => n.NetworkId).ToList());
                    var newConnectedNetworkIds = new HashSet<Guid>(newConnectedNetworks.Select(n => n.NetworkId).ToList());

                    if (!connectedNetworkIds.SetEquals(newConnectedNetworkIds))
                    {
                        connectedNetworks = newConnectedNetworks;

                        if (ValidateNetworksIds(connectedNetworkIds) && ValidateNetworksIds(newConnectedNetworkIds))
                        {
                            CaptivePortalDetected = false;

                            Debug.WriteLine("Network change detected: ");
                            foreach (var network in connectedNetworks)
                            {
                                Debug.WriteLine(network.Name + " : " + network.NetworkId);
                            }
                        }
                    }
                }
            });
        }

        private bool ValidateNetworksIds(HashSet<Guid> networkIds)
        {
            foreach (var id in networkIds)
            {
                try
                {
                    NetworkListManager.GetNetwork(id);
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }
    }
}
