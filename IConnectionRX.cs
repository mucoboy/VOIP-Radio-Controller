using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VOIP_Radio_Controller
{
    //RX interface to inform user about SIP session and receiving audio
    interface IConnectionRX
    {
        void onRXSIPStarted(string frequency, string rtpPort, string pttId);
        void onRXSIPStopped(string cause);
        void onAudioReceived();
        void onKeepReceived();
    }
}
