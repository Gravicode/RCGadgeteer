using Gadgeteer.Modules.GHIElectronics;
using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Presentation;
using Microsoft.SPOT.Presentation.Controls;
using Microsoft.SPOT.Presentation.Media;
using Microsoft.SPOT.Presentation.Shapes;
using Microsoft.SPOT.Touch;

using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;

using Microsoft.SPOT.Net.NetworkInformation;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using System.Text;

namespace System.Diagnostics
{
    public enum DebuggerBrowsableState
    {
        Never,
        Collapsed,
        RootHidden
    }
}
namespace MobileRC
{
    public class MobilRemote
    {
        public enum ArahJalan { Maju, Mundur, Kanan, Kiri, Stop}
        public ArahJalan Arah { set; get; }
        public int Kecepatan { set; get; }

        public MobilRemote()
        {
            Kecepatan = 0;
            Arah = ArahJalan.Stop;
        }
    }
    public partial class Program
    {
        const string SSID = "gravicode";
        const string KeyWifi = "123qweasd";
        const string MQTT_BROKER_ADDRESS = "192.168.100.4";
        static bool isNavigating = false;
        static MqttClient client { set; get; }
        static MobilRemote Mobil { set; get; }
        
        // This method is run when the mainboard is powered up or reset.   
        void ProgramStarted()
        {
            //setup wifi
            wifiRS21.DebugPrintEnabled = true;
            NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;           // setup events
            NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
            wifiRS21.NetworkDown += new GT.Modules.Module.NetworkModule.NetworkEventHandler(wifi_NetworkDown);
            wifiRS21.NetworkUp += new GT.Modules.Module.NetworkModule.NetworkEventHandler(wifi_NetworkUp);
            // use the router's DHCP server to set my network info
            if (!wifiRS21.NetworkInterface.Opened)
                wifiRS21.NetworkInterface.Open();
            if (!wifiRS21.NetworkInterface.IsDhcpEnabled)
            {
                wifiRS21.UseDHCP();
                wifiRS21.NetworkInterface.EnableDhcp();
                wifiRS21.NetworkInterface.EnableDynamicDns();
            }
            // look for avaiable networks
            var scanResults = wifiRS21.NetworkInterface.Scan();

            // go through each network and print out settings in the debug window
            foreach (GHI.Networking.WiFiRS9110.NetworkParameters result in scanResults)
            {
                Debug.Print("****" + result.Ssid + "****");
                Debug.Print("ChannelNumber = " + result.Channel);
                Debug.Print("networkType = " + result.NetworkType);
                Debug.Print("PhysicalAddress = " + GetMACAddress(result.PhysicalAddress));
                Debug.Print("RSSI = " + result.Rssi);
                Debug.Print("SecMode = " + result.SecurityMode);
            }

            // locate a specific network
            GHI.Networking.WiFiRS9110.NetworkParameters[] info = wifiRS21.NetworkInterface.Scan(SSID);
            if (info != null)
            {
                wifiRS21.NetworkInterface.Join(info[0].Ssid, KeyWifi);
                wifiRS21.UseThisNetworkInterface();
                bool res = wifiRS21.IsNetworkConnected;
                Debug.Print("Network joined");
                Debug.Print("active:" + wifiRS21.NetworkInterface.ActiveNetwork.Ssid);
           }

            Debug.Print("Program Started");
          
            Mobil = new MobilRemote();
            GT.Timer timer = new GT.Timer(100); 
            timer.Tick += (x) =>
            {
                if (isNavigating) return;
                isNavigating = true;
                ledStrip.TurnAllLedsOff();

                switch (Mobil.Arah)
                {
                    case MobilRemote.ArahJalan.Maju:
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor1, 1);
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor2, 1);
                        ledStrip.TurnAllLedsOn();
                        break;
                    case MobilRemote.ArahJalan.Mundur:
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor1, -0.7);
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor2, -0.7);
                        ledStrip.TurnLedOn(2);
                        ledStrip.TurnLedOn(3);
                        ledStrip.TurnLedOn(4);
                        break;
                    case MobilRemote.ArahJalan.Kiri:
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor1, -0.7);
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor2, 0.7);
                        ledStrip.TurnLedOn(0);
                        ledStrip.TurnLedOn(1);
                        break;
                    case MobilRemote.ArahJalan.Kanan:
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor1, 0.7);
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor2, -0.7);
                        ledStrip.TurnLedOn(5);
                        ledStrip.TurnLedOn(6);
                        break;
                    case MobilRemote.ArahJalan.Stop:
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor1, 0);
                        motorDriverL298.SetSpeed(MotorDriverL298.Motor.Motor2, 0);
                        
                        break;


                }
                isNavigating = false;
            };
            timer.Start();
            while (!wifiRS21.IsNetworkConnected || wifiRS21.NetworkInterface.IPAddress=="0.0.0.0")
            {
                Thread.Sleep(100);
            }
            client = new MqttClient(MQTT_BROKER_ADDRESS);
            string clientId = Guid.NewGuid().ToString();
            client.Connect(clientId);
            SubscribeMessage();
            
        }
        #region MQTT
        void SubscribeMessage()
        {
            // register to message received 
            client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived; ;
            client.Subscribe(new string[] { "/robot/control" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE });

        }

        private void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            string Message = new string(Encoding.UTF8.GetChars(e.Message));
            if (Message.IndexOf(":") < 1) return;
            // handle message received 
            Debug.Print("Message Received = " + Message);
            string[] CmdStr = Message.Split(':');
            if (CmdStr[0] == "MOVE")
            {
                if (Mobil == null) return;
                var ArahStr = string.Empty;
                switch (CmdStr[1])
                {
                    case "F":
                        Mobil.Arah = MobilRemote.ArahJalan.Maju;
                        ArahStr = "Maju";
                        break;
                    case "B":
                        Mobil.Arah = MobilRemote.ArahJalan.Mundur;
                        ArahStr = "Mundur";
                        break;
                    case "L":
                        Mobil.Arah = MobilRemote.ArahJalan.Kiri;
                        ArahStr = "Kiri";
                        break;
                    case "R":
                        Mobil.Arah = MobilRemote.ArahJalan.Kanan;
                        ArahStr = "Kanan";
                        break;
                    case "S":
                        Mobil.Arah = MobilRemote.ArahJalan.Stop;
                        ArahStr = "Stop";
                        break;
                }
                characterDisplay.Clear();
                characterDisplay.Print("Arah : " + ArahStr);
                PublishMessage("/robot/status", "Robot Status:" + CmdStr[1]);

            }
            else if (CmdStr[0] == "REQUEST" && CmdStr[1] == "STATUS")
            {
                PublishMessage("/robot/state", "ONLINE");
            }
        }

        void PublishMessage(string Topic, string Pesan)
        {
            client.Publish(Topic, Encoding.UTF8.GetBytes(Pesan), MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, false);
        }
        #endregion

        #region Network
        private void NetworkChange_NetworkAddressChanged(object sender, EventArgs e)
        {
            Debug.Print("Network address changed");
        }

        private void NetworkChange_NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            Debug.Print("Network availability: " + e.IsAvailable.ToString());
        }
        // handle the network changed events
        void wifi_NetworkDown(GT.Modules.Module.NetworkModule sender, GT.Modules.Module.NetworkModule.NetworkState state)
        {
            if (state == GT.Modules.Module.NetworkModule.NetworkState.Down)
                Debug.Print("Network Up event; state = Down");
            else
                Debug.Print("Network Up event; state = Up");
        }

        void wifi_NetworkUp(GT.Modules.Module.NetworkModule sender, GT.Modules.Module.NetworkModule.NetworkState state)
        {
            if (state == GT.Modules.Module.NetworkModule.NetworkState.Up)
            {
                Debug.Print("Network Up event; state = Up");
                Debug.Print("IP:" + wifiRS21.NetworkInterface.IPAddress);
            }
            else
                Debug.Print("Network Up event; state = Down");
        }

        // borrowed from GHI's documentation
        string GetMACAddress(byte[] PhysicalAddress)
        {
            return ByteToHex(PhysicalAddress[0]) + "-"
                                + ByteToHex(PhysicalAddress[1]) + "-"
                                + ByteToHex(PhysicalAddress[2]) + "-"
                                + ByteToHex(PhysicalAddress[3]) + "-"
                                + ByteToHex(PhysicalAddress[4]) + "-"
                                + ByteToHex(PhysicalAddress[5]);
        }

        string ByteToHex(byte number)
        {
            string hex = "0123456789ABCDEF";
            return new string(new char[] { hex[(number & 0xF0) >> 4], hex[number & 0x0F] });
        }
        #endregion
    }
}
