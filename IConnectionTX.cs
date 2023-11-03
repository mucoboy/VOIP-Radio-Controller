using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VOIP_Radio_Controller
{
    //TX interface to inform user about SIP Connection
    interface IConnectionTX
    {
        void onTXSIPStarted(string frequency, string rtpPort, string pttId);
        void onTXSIPStopped(string cause);
    }
}
