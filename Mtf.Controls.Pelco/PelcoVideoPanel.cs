using Mtf.Controls.Pelco.Enums;
using Mtf.Network;
using Mtf.Network.EventArg;
using Mtf.Network.Extensions;
using PelcoAPI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Mtf.Controls.Pelco
{
    [ToolboxItem(true)]
    [ToolboxBitmap(typeof(PelcoVideoPanel), "Resources.VideoSource.png")]
    public class PelcoVideoPanel : PictureBox
    {
        public const ushort PORT = 80;//49152;
        public static readonly string PluginDirectory = $@"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}\Pelco\API\Libs\Release\Plugins\";

        private const string Now = "NOW";
        private const string Infinite = "INFINITE";

        private PelcoAPIViewerNet viewer;
        private StreamInfoNet streamInfo;
        private Client client;

        private int cseq;
        private string sessionId;
        private string cameraIp;
        private ushort cameraPort;
        private string cameraUsername;
        private string cameraPassword;
        private int cameraStreamId;

        public PelcoVideoPanel()
        {
            SetStyle(ControlStyles.DoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            UpdateStyles();
            BackgroundImage = Properties.Resources.NoSignal;
            BackgroundImageLayout = ImageLayout.Stretch;
            SizeMode = PictureBoxSizeMode.StretchImage;
            viewer = new PelcoAPIViewerNet();
            viewer.SetWindowHandle(Handle);
            viewer.SetPluginDir(PluginDirectory);
            streamInfo = new StreamInfoNet
            {
                m_strStartTime = Now,
                m_strEndTime = Infinite
            };
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [Description("Protocol to be used for the connection.")]
        public RealTimeProtocol Protocol { get; set; } = RealTimeProtocol.RTSP;

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [Description("RTSP port number.")]
        public ushort RtspPort { get; set; } = 554;

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [Description("Whether a multicast or not.")]
        public bool Multicast { get; set; } = false;

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [Description("Camera service identifier.")]
        public string CameraServiceId { get; set; } = "1";

        [Description("Whether the camera is connected or not.")]
        public bool IsConnected => !String.IsNullOrEmpty(sessionId);

        public void Connect(string cameraIp = "192.168.0.20", ushort cameraPort = 80, string username = "admin", string password = "admin", int streamId = 1)
        {
            this.cameraIp = cameraIp;
            this.cameraPort = cameraPort;
            cameraUsername = username;
            cameraPassword = password;
            cameraStreamId = streamId;
        }

        public void Pause()
        {
            if (IsConnected)
            {
                _ = viewer?.Pause(sessionId);
            }
        }

        public void PauseLiveStream()
        {
            if (viewer != null)
            {
                Stop();
            }
        }

        public void ResumeLiveStream()
        {
            if (viewer != null)
            {
                StartSession();
            }
        }

        public void Stop()
        {
            if (IsConnected)
            {
                _ = viewer?.StopStream(sessionId);
            }
            sessionId = null;
        }

        public void PlayReverse()
        {
            if (IsConnected)
            {
                _ = viewer?.PlayReverse(sessionId, 1);
            }
        }

        public void PlayForward()
        {
            if (IsConnected)
            {
                _ = viewer?.PlayForward(sessionId, 1);
            }
        }

        public void FastReverse(float speed)
        {
            if (IsConnected)
            {
                _ = viewer?.PlayReverse(sessionId, speed);
            }
        }

        public void FastForward(float speed)
        {
            if (IsConnected)
            {
                _ = viewer?.PlayForward(sessionId, speed);
            }
        }

        public void Disconnect()
        {
            Stop();
            Invalidate();
            BeginInvoke((Action)(() => Image = null));
            if (client != null)
            {
                client.DataArrived -= Client_DataArrived;
                client.Dispose();
            }
        }

        public bool IsPtz()
        {
            return IsConnected && (viewer?.IsPTZCamera(sessionId) ?? false);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            viewer?.SetDisplayRect(0, 0, Width, Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsConnected)
                {
                    Disconnect();
                }
                viewer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void StartSession()
        {
            switch (Protocol)
            {
                case RealTimeProtocol.RTP:
                    sessionId = viewer?.StartStream(Now, Infinite, cameraIp, cameraPort.ToString(), CameraServiceId, null, null, null, null, true, false, streamInfo);
                    break;
                case RealTimeProtocol.RTSP:
                    //var rtspUrl = $"rtsp://{cameraIp}:{RtspPort}/?deviceid={cameraUuid}";
                    sessionId = viewer?.StartStream(GetStreamUrl(), cameraUsername, cameraPassword, Multicast, null);
                    break;
            }

            if (!IsConnected)
            {
                cseq = 1;
                client = new Client(cameraIp, RtspPort);
                client.DataArrived += Client_DataArrived;
                client.Send($"OPTIONS {GetStreamUrl()} RTSP/1.0\r\nCSeq: {cseq}\r\n\r\n");
                throw new InvalidOperationException("StartStream failed");
            }
        }

        private string GetStreamUrl() => $"rtsp://{cameraIp}:{RtspPort}/stream{cameraStreamId}";

        private void Client_DataArrived(object sender, DataArrivedEventArgs e)
        {
            if (e.Data == null || e.Data.Length == 0)
            {
                return;
            }

            try
            {
                var session = String.Empty;
                var received = client.Encoding.GetString(e.Data);
                if (!received.Contains("RTSP/1.0 200 OK"))
                {
                    throw new InvalidOperationException($"RTSP error: {received}");
                }

                cseq++;
                switch (cseq)
                {
                    case 2:
                        client.Send($"DESCRIBE {GetStreamUrl()} RTSP/1.0\r\nCSeq: {cseq}\r\n\r\n");
                        break;
                    case 3:
                        var track = received.ExtractBetween("lowLatency\r\na=control:", "\r\n");
                        var castType = Multicast ? "multicast" : "unicast";
                        client.Send($"SETUP {GetStreamUrl()}/{track} RTSP/1.0\r\nCSeq: {cseq}\r\nTransport:RTP/AVP;{castType};client_port=65534-65535\r\n\r\n");
                        break;
                    case 4:
                        session = received.ExtractBetween("Session:", "\r\n\r\n");
                        client.Send($"PLAY {GetStreamUrl()}/ RTSP/1.0\r\nCSeq: {cseq}\r\nSession: {session}\r\nRange: npt=0.000-\r\n\r\n");
                        break;
                    case 5:
                        client.Send($"TEARDOWN {GetStreamUrl()}/ RTSP/1.0\r\nCSeq: {cseq}\r\nSession: {session}\r\n\r\n");
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                Console.Error.WriteLine(ex.ToString());
                throw;
            }
        }
    }
}
