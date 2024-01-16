
using System;
using System.Collections;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.IsolatedStorage;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;


namespace Conductor
{
    public class MacroRunner
    {
        public string CurrentMacro;
        public Socket MRSocket = null;
        string response = "";
        StreamReader fs = null;
        NetworkStream ns = null;
        ConductorController controller = null;
        UInt16 m_crc;
        ConductorController rc;
        SemaphoreSlim waitForResponse = new SemaphoreSlim( 0 );
        string waitTarget = null;
        private readonly log4net.ILog _logger = log4net.LogManager.GetLogger( typeof( MacroRunner ) );
        public MacroRunner( ConductorController sc, string filename )
        {
            CurrentMacro = filename;
            fs = new StreamReader( CurrentMacro );
            controller = sc;
        }
        public MacroRunner( ConductorController sc, Socket socket )
        {

            CurrentMacro = null;
            MRSocket = socket;
            ns = new NetworkStream( socket );
            controller = sc;

        }

        private UInt16 crc_update( byte data )
        {
            int i;
            m_crc = (UInt16)(m_crc ^ ((UInt16)data << 8));
            for (i = 0 ; i < 8 ; i++)
            {
                if ((m_crc & 0x8000) != 0)
                    m_crc = (UInt16)((m_crc << 1) ^ 0x1021);
                else
                    m_crc <<= 1;
            }
            return m_crc;
        }


        public string readLine()
        {
            if (CurrentMacro == null)
            {
                byte[] myReadBuffer = new byte[2];
                string line = "";


                while (true)
                {
                    int numberOfBytesRead = ns.Read( myReadBuffer, 0, 1 );
                    if (numberOfBytesRead > 0)
                    {
                        if (myReadBuffer[0] == '\n')
                        {
                            break;
                        }
                        else
                        if (myReadBuffer[0] == '\r')
                        {
                            //swallow CR
                        }
                        else
                        if (myReadBuffer[0] == 0x03) //EOF
                            return null;
                        else
                            line += myReadBuffer[0];
                    }
                }
                return line;
            }
            else
                return fs.ReadLine();
        }


        // report progress to conductor, get response from him
        public string ReportProgress( string report, string sender )
        {
            byte[] myReadBuffer = new byte[2];
            string line = "";
            if (ns == null)    //no conductor active, do nothing
                return "";
            string localReport = "sender" + "|" + report + "\n\r";
            ns.Write( System.Text.Encoding.UTF8.GetBytes( localReport ), 0, localReport.Length ); //send report
            //read response from conductor
            while (true)
            {
                int numberOfBytesRead = ns.Read( myReadBuffer, 0, 1 );
                if (numberOfBytesRead > 0)
                {
                    if (myReadBuffer[0] == '\r')
                    {
                        break;
                    }
                    else
                    if (myReadBuffer[0] == 0x03) //EOF
                        return null;
                    else
                        line += myReadBuffer[0];
                }
            }
            return line;
        }

        public void RunMacro()
        {

            //  Read in macro stream

            byte[] b = new byte[1024];
            string line;
            byte[] sendBuffer = new byte[1024];
            while ((line = readLine()) != null)
            {
                Thread.Yield();
                if (line.StartsWith( "IFRETURNISNOT" )) //conditional execution based on last return
                {
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = parsedLine[1]; //isolate target value

                    if (value == response) //last return matches value
                        continue; //do nothing, go to read next command
                    //value is not equal to last response, execute conditional command
                    line = ""; //reassemble rest of conditional command
                    for (int i = 2 ; i < parsedLine.Length ; i++)
                    {
                        line += parsedLine[i];
                        if (i < parsedLine.Length - 1) line += ",";
                    }
                    //continue execution as if it was non-conditional
                }
                if (line.StartsWith( "IFRETURNIS" )) //conditional execution based on last return
                {
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = parsedLine[1]; //isolate target value

                    if (value != response) //last return does not match value
                        continue; //do nothing, go to read next command
                    //value is equal to last response
                    line = ""; //reassemble rest of command
                    for (int i = 2 ; i < parsedLine.Length ; i++)
                    {
                        line += parsedLine[i];
                        if (i < parsedLine.Length - 1) line += ",";
                    }
                    //coninue execution as if it was non-conditional
                }
                if (line.StartsWith( "LOGERROR" )) //write log entry
                {
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = parsedLine[1];

                    _logger.Error( value );
                    continue;
                }
                // "Nested" macro calling
                if (line.StartsWith( "@" ))
                {
                    MacroRunner macroRunner = new MacroRunner( controller, line.Substring( 1 ) );
                    macroRunner.RunMacro();
                    continue;
                }
                // Wait for fixed time
                if (line.StartsWith( "SLEEP" ))
                {
                    int delay = 0;
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        delay = Int32.Parse( parsedLine[1] );
                    Thread.Sleep( delay );
                    continue;
                }
                
                // Wait until specified status is read back from component specified
                // Message format is "WAIT,component,status"
                if (line.StartsWith( "WAIT" ))
                {
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                    {
                        while (true)
                            {
                                var player = controller.orchestra[parsedLine[1]];
                                if (player.lastResponse == parsedLine[2])
                                    break;
                            }; 
                    }
                    Thread.Yield();
                    continue;
                }
                // Pop up MessageBox
                if (line.StartsWith( "ALERT" ))
                {
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;

                    if (parsedLine[1] != null)
                    {
                        MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                        DialogResult result;
                        result = MessageBox.Show( parsedLine[1], "Alert!", buttons );
                        response = result.ToString();
                        continue;
                    }
                }
                // Pop up MessageBox
                if (line.StartsWith( "REPORT" ))
                {
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;

                    if (parsedLine[1] != null)
                    {
                        string result = ReportProgress( parsedLine[1], "Conductor" );
                        continue;
                    }
                }
                //Actual command
                //Isolate target component name
                string[] lin2 = line.Split( '#' ); //kill comments
                if (!string.IsNullOrWhiteSpace( lin2[0] ))
                {
                    string[] lin3 = lin2[0].Split( ':' );
                    if (lin3.Length < 2)
                        continue;
                    if (!controller.orchestra.ContainsKey( lin3[0] ))
                    {
                        Console.WriteLine( "Unknown component {0}, ignoring", lin3[0] );
                        continue;
                    }
                    musician target = controller.orchestra[lin3[0]];
                    int startCommand = lin2[0].IndexOf( ":" )+1;
                    string command = lin2[0].Substring( startCommand );
                    target.SendCommand( command );
                    controller.parent.updateStatus( lin3[0], command );
                }

            }
            fs.Close();
        }


    }

}
