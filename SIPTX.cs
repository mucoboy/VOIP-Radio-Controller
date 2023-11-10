using NAudio.Codecs;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace VOIP_Radio_Controller
{
    class SIPTX
    {
        UdpClient sipClient, rtpClient;
        ushort radioPort;
        IConnectionTX connection;
        string pcIp, radioIp, radioSipUser,pcSipUser,pcPort;
        string newLine = "\r\n";
        string callId;
        byte pttId = 64;
        public bool sipSession = false;
        bool onBroadcast = false;
        string pcRtpPort;
        WaveInEvent waveIn; //nAudio wave input
        IPEndPoint rtpEndPoint;
        int timeStamp;
        short seq;
        public SIPTX(IConnectionTX connection, string radioIp, ushort radioPort,  string pcIp, string radioSipUser, string pcSipUser)
        {
            this.connection = connection;
            this.radioIp = radioIp;
            this.radioPort = radioPort;
            this.pcIp = pcIp;
            this.radioSipUser = radioSipUser;
            this.pcSipUser = pcSipUser;

            //random timestamp and sequence number for every sip session
            Random random = new Random();
            int timeStamp = random.Next(2022, 1000000);//int
            short seq = (short)random.Next(150, 2022);//short

            //random callId according to pc ip and time
            callId = DateTime.UtcNow.Ticks.ToString() + pcIp.Substring(pcIp.LastIndexOf('.') + 1);

            //NAUDIO Library wawein event. waweformat is 8kHz and 16 bit pcm
            waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(8000, 16, 1);
            waveIn.BufferMilliseconds = 20;//framesize is 20ms
            waveIn.DataAvailable += WaveIn_DataAvailable; //wawein event method

            new Thread(startSIP).Start();//this thread will stop when sip session stopped
        }


        public void startSIP()
        {

            try
            {
                sipClient = new UdpClient(new IPEndPoint(IPAddress.Parse(pcIp), 0));
                sipClient.Client.ReceiveTimeout = 3000;//if we don't receive any packet in 3 sec, inform user
                rtpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(pcIp), 0));//use random port number
                rtpClient.Client.ReceiveTimeout = 2000;
                pcRtpPort = (rtpClient.Client.LocalEndPoint as IPEndPoint).Port.ToString();//random port number is sent to radio in invite message
                pcPort = (sipClient.Client.LocalEndPoint as IPEndPoint).Port.ToString();//random sip port

                //declare radio endpoint according to ip and port
                var radioEndPoint = new IPEndPoint(IPAddress.Parse(radioIp), radioPort);

                //first invite message
                var inviteMessage = getInviteMessage();
                sipClient.Send(inviteMessage, inviteMessage.Length, radioEndPoint);

                var receivedBytes = sipClient.Receive(ref radioEndPoint);
                var receivedString = Encoding.UTF8.GetString(receivedBytes);//convert to string

                //we will wait 200 OK message. as our receivetimeout is 3 sec, if we don't receive anything in 3 sec, go to catch
                while (!receivedString.StartsWith("SIP/2.0 200 OK"))
                {
                    receivedBytes = sipClient.Receive(ref radioEndPoint);
                    receivedString = Encoding.UTF8.GetString(receivedBytes);
                }

                var tag = receivedString.Substring(receivedString.LastIndexOf(";tag="));
                tag = tag.Substring(tag.IndexOf('=') + 1, tag.IndexOf("\r\n") - 5);
                var rtpPort = receivedString.Substring(receivedString.IndexOf("m=audio"));
                rtpPort = rtpPort.Substring(0, rtpPort.IndexOf("RTP")).Remove(0, 8).Trim();

                var Id = receivedString.Substring(receivedString.IndexOf("a=ptt-id:") + 9, 1);

                if (Id == "1")
                    pttId = 64;
                else if (Id == "2")
                    pttId = 128;
                else if (Id == "3")
                    pttId = 192;

                var frequency = receivedString.Substring(receivedString.IndexOf("a=Fid:") + 6, 7);

                //inform user
                connection.onTXSIPStarted(frequency, rtpPort, Id);
                sipSession = true;

                //send ack message to radio and start rtp session
                var ackMessage = getAckMessage(tag);
                sipClient.Send(ackMessage, ackMessage.Length, radioEndPoint);

                rtpEndPoint = new IPEndPoint(IPAddress.Parse(radioIp), Convert.ToInt32(rtpPort));

                new Thread(sendKeepAlive).Start();//this thread stops if sipSession=false

                //if we don't receive anything in 2 sec, stop sip session
                while (sipSession)
                {
                    rtpClient.Receive(ref rtpEndPoint);

                }


            }
            catch (SocketException ex)
            {
                sipSession = false;
                onBroadcast = false;

                //user action
                if (ex.SocketErrorCode == SocketError.Interrupted)
                    connection.onTXSIPStopped("user action");

                else if (ex.SocketErrorCode == SocketError.Fault)
                    connection.onTXSIPStopped("socket fault. try another adapter!");


                else if (ex.SocketErrorCode == SocketError.AddressNotAvailable)
                    connection.onTXSIPStopped("address not available. try another adapter!");

                else if (ex.SocketErrorCode == SocketError.TimedOut)
                    connection.onTXSIPStopped("time out. check radio connection!");

                else
                    MessageBox.Show("socket error code -> " + ex.SocketErrorCode.ToString());
            }
            catch (Exception ex)
            {
                connection.onTXSIPStopped(ex.ToString());
                stopSIP();

            }

        }

        //if there is no broadcast, send keep alive
        private void sendKeepAlive()
        {
            while (sipSession)
            {
                if (!onBroadcast)
                {
                    //rtp keep alive 
                    byte[] header = new byte[20];//12 byte standard rtp, ext 20 byte
                    header[0] = 144;//ext 
                    header[1] = 0x7b;//8-alaw 11-pcm 0-ulaw
                    header[2] = 0;
                    header[3] = 130; //sequen 2-3
                    header[12] = 1;//ed137
                    header[13] = 103;
                    header[15] = 1;
                    header[16] = 0;//32
                    header[17] = pttId;//64

                    header[4] = (byte)(timeStamp >> 24);
                    header[5] = (byte)(timeStamp >> 16);
                    header[6] = (byte)(timeStamp >> 8);
                    header[7] = (byte)timeStamp;

                    header[2] = (byte)(seq >> 8);
                    header[3] = (byte)seq;

                    seq++;

                    timeStamp += 1600; //200 ms = 1600 byte
                    rtpClient.Send(header, header.Length, rtpEndPoint);
                }

                Thread.Sleep(200);
            }
        }

        //When audio received from mic, send to radio
        private void WaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                //we will convert the buffer from short to byte, so it must be even
                if (e.Buffer.Length % 2 != 0)
                    return;

                //encode data then convert to byte from short
                var encoded = new byte[e.Buffer.Length/2];

                for(int i = 0; i < e.Buffer.Length; i+=2)
                {
                    var sample = ALawEncoder.LinearToALawSample((short)(e.Buffer[i+1]*256+e.Buffer[i]));
                    encoded[i / 2] = sample;
                }


                byte[] header = new byte[20];//160 bit
                header[0] = 144;//ext
                header[1] = 8;//8-alaw 11-pcm
                header[2] = 0;
                header[3] = 130; //sequen 2-3
                header[12] = 1;//ed137
                header[13] = 103;
                header[15] = 1;
                header[16] = 32;//32
                header[17] = pttId;

                byte[] rtpPacket = new byte[20 + 160];//160 = 20 ms
                header[4] = (byte)(timeStamp >> 24);
                header[5] = (byte)(timeStamp >> 16);
                header[6] = (byte)(timeStamp >> 8);
                header[7] = (byte)timeStamp;

                header[2] = (byte)(seq >> 8);
                header[3] = (byte)seq;

                Array.Copy(header, 0, rtpPacket, 0, 20);
                Array.Copy(encoded, 0, rtpPacket, 20, 160);
                
                seq++;
                timeStamp += 160;

                rtpClient.Send(rtpPacket, rtpPacket.Length, rtpEndPoint);
                
            }
            catch (Exception)
            {

                
            }

            
        }

        //invite message contains our sip address and port, rtp adddress and port, alaw codec, keep alive period so on..
       private byte[] getInviteMessage()
        {
            //SDP=Session Description Protocol 
            string inviteSDP = "v=0" + newLine
                + "o=" + pcSipUser + " IN IP4 " + pcIp + newLine
                + "s=conversation" + newLine
                + "c=IN IP4 " + pcIp + newLine
                + "t=0 0" + newLine
                + "m=audio " + pcRtpPort + " RTP/AVP 8 123" + newLine//8 g711 alaw
                + "a=rtpmap:8 G711/8000" + newLine
                + "a=rtpmap:123 R2S/8000" + newLine
                + "a=sendrecv" + newLine
                + "a=type:Radio-TxRx" + newLine
                + "a=txrxmode:Tx" + newLine
                + "a=bss:RSSI" + newLine
                + "a=sigtime:1" + newLine
                + "a=ptt_rep:0" + newLine
                + "a=rtphe:1" + newLine
                + "a=R2S-KeepAlivePeriod:200" + newLine
                + "a=R2S-KeepAliveMultiplier:10" + newLine;

            string inviteMessage = "INVITE sip:" + radioSipUser + "@" + radioIp + ":" + radioPort.ToString() + " SIP/2.0" + newLine
                + "Via: SIP/2.0/UDP " + pcIp + ":"+pcPort + ";rport;branch=b" + pcIp + newLine
                + "Max-Forwards: 70" + newLine
                + "From: <sip:" + pcSipUser + "@" + pcIp + ":"+pcPort+ ">;tag=t" + pcIp + newLine
                + "To: <sip:" + radioSipUser + "@" + radioIp + ":"+ radioPort + ">" + newLine
                + "Call-ID: " + callId + newLine
                + "CSeq: " + pcIp.Substring(pcIp.LastIndexOf('.') + 1) + " INVITE" + newLine
                + "Contact: <sip:" + pcSipUser + "@" + pcIp + ":" + pcPort + ">" + newLine
                + "Subject: radio" + newLine
                + "WG67-Version: radio.01" + newLine
                + "Priority: normal" + newLine
                + "User-Agent: mucoSip" + newLine
                + "Accept: application/sdp, message/sipfrag, text/plain, text/*, application/conference-info+xml, application/pidf+xml, application/dialog-info+xml" + newLine
                + "Allow: INVITE, OPTIONS, MESSAGE, SUBSCRIBE, NOTIFY, ACK, BYE, CANCEL" + newLine
                + "Supported: events, 100rel" + newLine
                + "Allow-Events: WG67KEY-IN" + newLine
                + "Content-Type: application/sdp" + newLine
                + "Content-Disposition: session" + newLine
                + "Content-Length: " + inviteSDP.Length.ToString() + newLine + newLine
                + inviteSDP;

            return Encoding.UTF8.GetBytes(inviteMessage);
        }

        //after ack message, rtp session started
        private byte[] getAckMessage(string tag)
        {
            var ackMessage = "ACK sip:"+radioSipUser+"@" + radioIp + ":"+radioPort + " SIP/2.0" + newLine
                         + "Via: SIP/2.0/UDP " + pcIp + ":"+pcPort + ";rport;branch=b" + pcIp + newLine
                         + "Max-Forwards: 70" + newLine
                         + "From: <sip:"+pcSipUser+"@" + pcIp + ":"+pcPort + ">;tag=t" + pcIp + newLine
                         + "To: <sip:"+radioSipUser+"@" + radioIp + ":"+radioPort + ">;tag=" + tag + newLine
                         + "Call-ID: " + callId + newLine
                         + "CSeq: " + pcIp.Substring(pcIp.LastIndexOf('.') + 1) + " ACK" + newLine
                         + "WG67-Version: radio.01" + newLine
                         + "Accept: application/sdp, message/sipfrag, text/plain, text/*, application/conference-info+xml, application/pidf+xml, application/dialog-info+xml" + newLine
                         + "Allow-Events: WG67KEY-IN" + newLine
                         + "Content-Length: 0" + newLine + newLine;

            return Encoding.UTF8.GetBytes(ackMessage); 
        }

        //when ptt button checked, start recording and sending
        public void startRecording()
        {
            try
            {
                onBroadcast = true;
                waveIn.StartRecording();
            }
            catch (Exception)
            {

                
            }
        }

        //when ptt button unchecked, stop recording
        public void stopRecording()
        {
            try
            {
                onBroadcast = false;
                waveIn.StopRecording();
            }
            catch (Exception)
            {

                
            }
        }

        //when sip session stopped, release all resources
        public void stopSIP()
        {
            try
            {
                if (sipClient != null)
                    sipClient.Close();
                if (rtpClient!=null)
                    rtpClient.Close();
                
                onBroadcast = false;
                sipSession = false;
                waveIn.StopRecording();
            }
            catch (Exception)
            {
                onBroadcast = false;
                sipSession = false;
            }
        }
    }
}
