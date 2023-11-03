using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VOIP_Radio_Controller
{
    //Implement Connection interfaces to inform user
    public partial class MainWindow : Window,IConnectionRX,IConnectionTX
    {
        SIPTX sipTx;
        SIPRX sipRx;
        public MainWindow()
        {
            InitializeComponent();

            getIP();//fill ip combobox with your ipv4 addresses
            
        }

        #region Methods
        void getIP()
        {
            try
            {
                ObservableCollection<string> list = new ObservableCollection<string>();

                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    IPInterfaceProperties ipProps = netInterface.GetIPProperties();

                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)//only ipv4
                            list.Add(addr.Address.ToString());
                    }


                }

                if (list.Count > 0)
                {
                    txIpCombo.ItemsSource = list;
                    txIpCombo.SelectedIndex = 0;
                    rxIpCombo.ItemsSource = list;
                    rxIpCombo.SelectedIndex = 0;
                }
            }
            catch (Exception)
            {

                MessageBox.Show("ip addresses can not listed! check your adapters!");
            }
        }
        #endregion

        #region Events

        //Start/Stop Tx SIP Session
        private void startTxSipButton_Click(object sender, RoutedEventArgs e)
        {
            startTxSipButton.IsChecked = !startTxSipButton.IsChecked;//it will checked/unchecked according to sip session response

            if (sipTx == null || (sipTx != null && !sipTx.sipSession))
            {
                if (IPAddress.TryParse(txIpCombo.SelectedItem as string, out IPAddress pcIp)
                && IPAddress.TryParse(radioIpText.Text, out IPAddress radioIp)
                && ushort.TryParse(radioPortText.Text, out ushort radioPort))
                    sipTx = new SIPTX(this, radioIpText.Text, radioPort, txIpCombo.SelectedItem as string, radioSipText.Text, pcSipText.Text);

                else
                {
                    MessageBox.Show("Invalid Ip or Port number!");

                }
            }

            else
            {
                if (sipTx != null)
                    sipTx.stopSIP();
            }
        }

        //Start/Stop RX SIP Session
        private void startRxSipButton_Click(object sender, RoutedEventArgs e)
        {
            startRxSipButton.IsChecked = !startRxSipButton.IsChecked;//it will checked/unchecked according to sip session response

            if (sipRx == null || (sipRx != null && !sipRx.sipSession))
            {
                if (IPAddress.TryParse(rxIpCombo.SelectedItem as string, out IPAddress pcIp)
                && IPAddress.TryParse(rxRadioIp.Text, out IPAddress radioIp)
                && ushort.TryParse(rxRadioPortText.Text, out ushort radioPort))
                    sipRx = new SIPRX(this, rxRadioIp.Text, radioPort, rxIpCombo.SelectedItem as string, rxRadioSipText.Text, rxPcSipText.Text);

                else
                    MessageBox.Show("Invalid Ip or Port number!");
            }

            else
            {
                if (sipRx != null)
                    sipRx.stopSIP();
            }

        }

        //PTT ON/OFF
        private void txPttButton_Click(object sender, RoutedEventArgs e)
        {
            txPttButton.IsChecked = !txPttButton.IsChecked;//first check status of sip session

            //only used if a sip session available 
            if (sipTx != null && sipTx.sipSession && txPttButton.IsChecked == false)
            {
                sipTx.startRecording();
                txPttButton.IsChecked = true;
            }

            else if (sipTx != null && sipTx.sipSession && txPttButton.IsChecked == true)
            {
                sipTx.stopRecording();
                txPttButton.IsChecked = false;
            }

            else
            {
                MessageBox.Show("Sip Session must be started!");
                txPttButton.IsChecked = false;
            }
        }

        //Change ptt button contents when it is checked/unchecked
        private void txPttButton_Checked(object sender, RoutedEventArgs e)
        {
            txPttButton.Background = Brushes.Green;
            txPttButton.Foreground = Brushes.White;
            txPttButton.Content = "PTT ON!";
        }

        private void txPttButton_Unchecked(object sender, RoutedEventArgs e)
        {

            txPttButton.Background = Brushes.LightGray;
            txPttButton.Foreground = Brushes.Black;
            txPttButton.Content = "PTT OFF!";

        }

        //Change tx sip button contents when it is checked/unchecked
        private void startTxSipButton_Checked(object sender, RoutedEventArgs e)
        {
            startTxSipButton.Background = Brushes.OrangeRed;
            startTxSipButton.Content = "Stop SIP Session";
        }

        private void startTxSipButton_Unchecked(object sender, RoutedEventArgs e)
        {
            startTxSipButton.Background = Brushes.LightGreen;
            startTxSipButton.Content = "Start SIP Session";
        }

        //Change rx sip button contents when it is checked/unchecked
        private void startRxSipButton_Checked(object sender, RoutedEventArgs e)
        {
            startRxSipButton.Background = Brushes.OrangeRed;
            startRxSipButton.Content = "Stop SIP Session";
        }

        private void startRxSipButton_Unchecked(object sender, RoutedEventArgs e)
        {
            startRxSipButton.Background = Brushes.LightGreen;
            startRxSipButton.Content = "Start SIP Session";
        }

        //release all resources when windows closed
        private void Window_Closed(object sender, EventArgs e)
        {
            if (sipTx != null && sipTx.sipSession)
                sipTx.stopSIP();
            if (sipRx != null && sipRx.sipSession)
                sipRx.stopSIP();
            Process.GetCurrentProcess().Kill();
        }

        #endregion

        #region IConnectionTX implementation

        //on tx sip started, inform user according to radio data
        void IConnectionTX.onTXSIPStarted(string frequency, string rtpPort, string pttId)
        {
            //Dispatcher for thread safety
            Dispatcher.Invoke(() => {
                txPttButton.IsChecked = false;
                startTxSipButton.IsChecked = true;
                txFrequencyLabel.Content = frequency;
                txIdLabel.Content = pttId;
                txtPortLabel.Content = rtpPort;
                txStatusLabel.Content = "Status: Sip Session Established!";
                txStatusLabel.Background = Brushes.Green;
            });
        }

        //on tx sip stopped, restart everything
        void IConnectionTX.onTXSIPStopped(string cause)
        {
            Dispatcher.Invoke(() => {
                txPttButton.IsChecked = false;
                startTxSipButton.IsChecked = false;
                txFrequencyLabel.Content = "unassigned";
                txIdLabel.Content = "unassigned";
                txtPortLabel.Content = "unassigned";
                txStatusLabel.Content = "Status: Waiting for Session!";
                txStatusLabel.Background = Brushes.Red;
            });

            if (cause != "user action")
                MessageBox.Show("Fault: " + cause);
        }
        #endregion

        #region IConnectionRX implementation

        //on rx sip started, inform user according to radio data
        public void onRXSIPStarted(string frequency, string rtpPort, string pttId)
        {
            Dispatcher.Invoke(() => {
                startRxSipButton.IsChecked = true;
                rxInfoLabel.Background = Brushes.LightGray;
                rxInfoLabel.Foreground = Brushes.Black;
                rxInfoLabel.Content = "Silent!";
                rxFrequencyLabel.Content = frequency;
                rxIdLabel.Content = pttId;
                rxtPortLabel.Content = rtpPort;
                rxStatusLabel.Content = "Status: Sip Session Established!";
                rxStatusLabel.Background = Brushes.Green;
            });
        }

        //on rx sip stopped, restart everything
        void IConnectionRX.onRXSIPStopped(string cause)
        {
            Dispatcher.Invoke(() => {
                startRxSipButton.IsChecked = false;
                rxInfoLabel.Background = Brushes.LightGray;
                rxInfoLabel.Foreground = Brushes.Black;
                rxInfoLabel.Content = "Silent!";
                rxFrequencyLabel.Content = "unassigned";
                rxIdLabel.Content = "unassigned";
                rxtPortLabel.Content = "unassigned";
                rxStatusLabel.Content = "Status: Waiting for Session!";
                rxStatusLabel.Background = Brushes.Red;
            });

            if (cause != "user action")
                MessageBox.Show("Fault: " + cause);
        }

        //when audio received, inform user
        void IConnectionRX.onAudioReceived()
        {
            rxInfoLabel.Dispatcher.Invoke(() => {
                rxInfoLabel.Background = Brushes.Green;
                rxInfoLabel.Foreground = Brushes.White;
                rxInfoLabel.Content = "ON AIR!";
            });
        }

        //when keep alive received, it means no audio received
        public void onKeepReceived()
        {
            rxInfoLabel.Dispatcher.Invoke(() => {

                rxInfoLabel.Background = Brushes.LightGray;
                rxInfoLabel.Foreground = Brushes.Black;
                rxInfoLabel.Content = "Silent!";

            });
        }

        #endregion

        
    }
}
