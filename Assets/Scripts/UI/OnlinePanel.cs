using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using Lidgren.Network;
using Sanicball.Data;
using Sanicball.Logic;
using UnityEngine;
using UnityEngine.UI;

namespace Sanicball.UI
{
    public class OnlinePanel : MonoBehaviour
    {
        public Transform targetServerListContainer;
        public Text errorField;
        public Text serverCountField;
        public ServerListItem serverListItemPrefab;
        public Selectable aboveList;
        public Selectable belowList;

        private List<ServerListItem> servers = new List<ServerListItem>();

        //Stores server browser IPs, so they can be differentiated from LAN servers
        private List<string> serverBrowserIPs = new List<string>();

        private NetClient discoveryClient;
        private ServerBrowserRequest serverBrowserRequester;
        private DateTime latestLocalRefreshTime;
        private DateTime latestBrowserRefreshTime;
        private static bool tlsConfigured;

        private static void TryEnableModernTls()
        {
            if (tlsConfigured)
            {
                return;
            }
            try
            {
                const SecurityProtocolType tls11 = (SecurityProtocolType)768;
                const SecurityProtocolType tls12 = (SecurityProtocolType)3072;
                ServicePointManager.SecurityProtocol |= tls11 | tls12;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Unable to enable modern TLS support: " + ex.Message);
            }
            finally
            {
                tlsConfigured = true;
            }
        }

        public void RefreshServers()
        {
            TryEnableModernTls();
            serverBrowserIPs.Clear();
            errorField.enabled = false;

            if (discoveryClient != null)
            {
                try
                {
                    discoveryClient.DiscoverLocalPeers(25000);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to start local server discovery - " + ex.Message);
                }
            }
            latestLocalRefreshTime = DateTime.Now;

            serverBrowserRequester = null;
            if (!string.IsNullOrEmpty(ActiveData.GameSettings.serverListURL))
            {
                try
                {
                    serverBrowserRequester = new ServerBrowserRequest(ActiveData.GameSettings.serverListURL);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to begin server list request - " + ex.Message);
                    errorField.enabled = true;
                    errorField.text = "Cannot query server list.";
                    serverCountField.text = "Cannot access server list URL!";
                }
            }
            else
            {
                errorField.enabled = true;
                errorField.text = "Server list URL missing.";
                serverCountField.text = "Server list unavailable";
            }

            if (serverBrowserRequester != null)
            {
                serverCountField.text = "Refreshing servers, hang on...";
            }

            //Clear old servers
            foreach (var serv in servers)
            {
                Destroy(serv.gameObject);
            }
            servers.Clear();
        }

        private void Awake()
        {
            errorField.enabled = false;

            NetPeerConfiguration config = new NetPeerConfiguration(OnlineMatchMessenger.APP_ID);
            config.EnableMessageType(NetIncomingMessageType.DiscoveryResponse);
            try
            {
                discoveryClient = new NetClient(config);
                discoveryClient.Start();
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to start discovery client - " + ex.Message);
                discoveryClient = null;
            }
        }

        private void Update()
        {
            //Refresh on f5 (pretty nifty)
            if (Input.GetKeyDown(KeyCode.F5))
            {
                RefreshServers();
            }

            //Check for response from the server browser requester
            if (serverBrowserRequester != null && serverBrowserRequester.IsDone)
            {
                if (string.IsNullOrEmpty(serverBrowserRequester.Error))
                {
                    latestBrowserRefreshTime = DateTime.Now;

                    string result = serverBrowserRequester.Result;
                    string[] entries = result.Split(new string[] { "<br>" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string entry in entries)
                    {
                        int seperationPoint = entry.LastIndexOf(':');
                        if (seperationPoint <= 0 || seperationPoint >= entry.Length - 1)
                        {
                            string trimmedEntry = entry.Length > 120 ? entry.Substring(0, 120) + "..." : entry;
                            Debug.LogWarning("Ignoring malformed server entry: " + trimmedEntry);
                            continue;
                        }
                        string ip = entry.Substring(0, seperationPoint);
                        string port = entry.Substring(seperationPoint + 1, entry.Length - (seperationPoint + 1));

                        int portInt;
                        if (int.TryParse(port, out portInt))
                        {
                            if (discoveryClient != null)
                            {
                                try
                                {
                                    discoveryClient.DiscoverKnownPeer(ip, portInt);
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogError("Failed to discover known peer " + ip + ":" + portInt + " - " + ex.Message);
                                }
                            }
                            serverBrowserIPs.Add(ip);
                        }
                    }
                    serverCountField.text = "0 servers";
                }
                else
                {
                    Debug.LogError("Failed to receive servers - " + serverBrowserRequester.Error);
                    serverCountField.text = "Cannot access server list URL!";
                }

                serverBrowserRequester = null;
            }

            //Check for messages on the discovery client
            if (discoveryClient == null)
            {
                return;
            }

            try
            {
                NetIncomingMessage msg;
                while ((msg = discoveryClient.ReadMessage()) != null)
                {
                    switch (msg.MessageType)
                    {
                        case NetIncomingMessageType.DiscoveryResponse:
                            ServerInfo info;
                            try
                            {
                                info = Newtonsoft.Json.JsonConvert.DeserializeObject<ServerInfo>(msg.ReadString());
                            }
                            catch (Newtonsoft.Json.JsonException ex)
                            {
                                Debug.LogError("Failed to deserialize info for a server: " + ex.Message);
                                continue;
                            }

                            //double timeDiff = (DateTime.UtcNow - info.Timestamp).TotalMilliseconds;
                            bool isLocal = !serverBrowserIPs.Contains(msg.SenderEndPoint.Address.ToString());

                            DateTime timeToCompareTo = isLocal ? latestLocalRefreshTime : latestBrowserRefreshTime;
                            double timeDiff = (DateTime.Now - timeToCompareTo).TotalMilliseconds;

                            var server = Instantiate(serverListItemPrefab);
                            server.transform.SetParent(targetServerListContainer, false);
                            server.Init(info, msg.SenderEndPoint, (int)timeDiff, isLocal);
                            servers.Add(server);
                            RefreshNavigation();

                            serverCountField.text = servers.Count + (servers.Count == 1 ? " server" : " servers");

                            break;

                        default:
                            Debug.Log("Server discovery client received an unhandled NetMessage (" + msg.MessageType + ")");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Server discovery failed - " + ex.Message);
            }
        }

        private void RefreshNavigation()
        {
            for (var i = 0; i < servers.Count; i++)
            {
                var button = servers[i].GetComponent<Button>();
                if (button)
                {
                    var nav = new Navigation() { mode = Navigation.Mode.Explicit };
                    //Up navigation
                    if (i == 0)
                    {
                        nav.selectOnUp = aboveList;
                        var nav2 = aboveList.navigation;
                        nav2.selectOnDown = button;
                        aboveList.navigation = nav2;
                    }
                    else
                    {
                        nav.selectOnUp = servers[i - 1].GetComponent<Button>();
                    }
                    //Down navigation
                    if (i == servers.Count - 1)
                    {
                        nav.selectOnDown = belowList;
                        var nav2 = belowList.navigation;
                        nav2.selectOnUp = button;
                        belowList.navigation = nav2;
                    }
                    else
                    {
                        nav.selectOnDown = servers[i + 1].GetComponent<Button>();
                    }

                    button.navigation = nav;
                }
            }
        }

        private sealed class ServerBrowserRequest
        {
            private volatile bool isDone;

            public ServerBrowserRequest(string url)
            {
                Result = string.Empty;
                Error = null;
                ThreadPool.QueueUserWorkItem(FetchServerList, url);
            }

            public bool IsDone
            {
                get { return isDone; }
            }

            public string Result { get; private set; }

            public string Error { get; private set; }

            private void FetchServerList(object state)
            {
                try
                {
                    var request = WebRequest.Create(state as string);
                    if (request != null)
                    {
                        request.Timeout = 10000;
                        using (var response = request.GetResponse())
                        using (var stream = response.GetResponseStream())
                        {
                            if (stream != null)
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    Result = reader.ReadToEnd();
                                }
                            }
                            else
                            {
                                Error = "Empty response stream.";
                            }
                        }
                    }
                    else
                    {
                        Error = "Invalid server list request.";
                    }
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
                finally
                {
                    isDone = true;
                }
            }
        }
    }
}
