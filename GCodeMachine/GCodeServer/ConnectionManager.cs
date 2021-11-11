using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Json;
using System.Net;
using System.Net.Sockets;
using PacketSender;

namespace GCodeServer
{
    public class Connection
    {
        private TcpClient tcpClient;
        private UdpClient udpClient;
        private SerialPort sport;
        public IPacketSender writer;
        public IPacketReceiver reader;

        public Connection(JsonValue config)
        {
            string proto = config["proto"];
            switch (proto)
            {
                case "TCP":
                    {
                        string addr = config["addr"];
                        int port = config["port"];
                        tcpClient = new TcpClient();
                        tcpClient.Connect(IPAddress.Parse(addr), port);
                        reader = new TcpPacketReceiver(tcpClient);
                        writer = new TcpPacketSender(tcpClient);
                        break;
                    }
                case "UDP":
                    {
                        string addr = config["addr"];
                        int port = config["port"];
                        udpClient = new UdpClient();
                        udpClient.Connect(IPAddress.Parse(addr), port);
                        reader = new UDPPacketReceiver(udpClient, addr, port);
                        writer = new UDPPacketSender(udpClient);
                        break;
                    }
                case "UART":
                    {
                        string port = config["port"];
                        sport = new SerialPort(port)
                        {
                            StopBits = StopBits.One,
                            BaudRate = config["baudrate"],
                            Parity = Parity.None,
                            DataBits = 8
                        };
                        sport.Open();
                        System.Threading.Thread.Sleep(2000);
                        reader = new SerialPacketReceiver(sport);
                        writer = new SerialPacketSender(sport);
                        break;
                    }
                default:
                    {
                        throw new ArgumentOutOfRangeException();
                    }
            }
        }

        public void Close()
        {
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient = null;
            }
            if (sport != null)
            {
                sport.Close();
                sport = null;
            }
        }
    }

    public class ConnectionManager
    {
        private List<Connection> connections;
        private Dictionary<string, int> usedConnections;

        public IReadOnlyDictionary<string, Connection> Connections
        {
            get
            {
                var d = new Dictionary<string, Connection>();
                foreach (var name in usedConnections)
                {
                    d[name.Key] = connections[usedConnections[name.Key]];
                }
                return d;
            }
        }

        public ConnectionManager()
        {
            connections = new List<Connection>();
            usedConnections = new Dictionary<string, int>();
        }

        private bool IsEqual(JsonValue config1, JsonValue config2)
        {
            foreach (KeyValuePair<string, JsonValue> kv in config1)
            {
                var key = kv.Key;
                if (!config2.ContainsKey(key))
                    return false;

                var val1 = kv.Value.ToString();
                var val2 = config2[key].ToString();

                if (val1 != val2)
                    return false;
            }
            return true;
        }

        public void CreateConnections(JsonValue config)
        {
            connections.Clear();
            usedConnections.Clear();
            List<JsonValue> configs = new List<JsonValue>();
            foreach (KeyValuePair<string, JsonValue> kv in config)
            {
                var name = kv.Key;
                JsonValue cfgConfig = kv.Value;
                int id;
                for (id = 0; id < configs.Count; id++)
                {
                    if (IsEqual(cfgConfig, configs[id]))
                        break;
                }
                if (id == configs.Count)
                {
                    // Not found equal, create new
                    configs.Add(cfgConfig);
                }
                usedConnections[name] = id;
            }

            foreach (var cfg in configs)
            {
                connections.Add(new Connection(cfg));
            }
        }

        public void Disconnect()
        {
            foreach (var conn in connections)
            {
                conn.Close();
            }
            connections.Clear();
            usedConnections.Clear();
        }
    }
}
