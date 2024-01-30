
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.IsolatedStorage;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;


namespace Conductor
{
    public class MacroRunner
    {
        public class MyRef<T>
        {
            public T Ref { get; set; }
        }


        public void refreshGUI()
        {
            this.controller.parent.Invalidate();
            this.controller.parent.Update();
            this.controller.parent.Refresh();
            Application.DoEvents();
        }



        public void AddVar( string key, object v ) //storing the ref to a string
        {
            if (null == v)
            {
                v = new MyRef<string> { Ref = " " };
            }
            variables.Add( key, v );
        }



        public void changeVar( string key, object newValue ) //changing any of them
        {
            var ref2 = variables[key] as MyRef<string>;
            if (ref2 == null)
            {
                ref2 = new MyRef<string> { Ref = " " };
            }
            ref2.Ref = newValue.ToString();
        }
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
        private string[] Macro;
        private int currentline = 0;
        private System.Collections.Generic.Dictionary<string, int> label = new System.Collections.Generic.Dictionary<string, int>();
        private Dictionary<string, object> variables = new Dictionary<string, object>();

        private string ExpandVariables( string instring )
        {
            StringBuilder sb = new StringBuilder();
            int start = 0;
            int i;

            var val = new MyRef<string> { Ref = "" };
            for (i = start ; i < instring.Length ; i++)
                if (instring[i] == '%')
                    for (int j = 1 ; j < instring.Length - i ; j++)
                        if (instring[i + j] == '%')
                        {
                            sb.Append( instring.Substring( start, i - start ) );
                            string key = instring.Substring( i + 1, j - 1 );
                            if (variables.ContainsKey( key ))
                            {
                                val = (MyRef<string>)variables[key];
                                sb.Append( val.Ref );
                                start = i = i + j + 1;
                            }
                            else _logger.Error( "Unknown variable:" + val );
                            continue;
                        }
            if ((i - start > 0) && (start < instring.Length))
                sb.Append( instring.Substring( start, i - start ) );
            return sb.ToString();
        }

        private string Evaluate( string instring )
        {
            instring = ExpandVariables( instring );
            DataTable dt = new DataTable();
            var v = dt.Compute( instring, "" );
            return v.ToString();
        }
        public MacroRunner( ConductorController sc, string filename )
        {
            CurrentMacro = filename;
            fs = new StreamReader( CurrentMacro );
            controller = sc;
            int currentline = 0;
            if (CurrentMacro != null)
            {
                //Load full macro into memory as array of strings
                Macro = System.IO.File.ReadAllLines( CurrentMacro );
                //Scan macro array for labels, record their line number in Dictionary
                currentline = 0;
                foreach (string line in Macro)
                {
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    if (line1[0].StartsWith( ":" ))
                        label.Add( line1[0].Substring( 1 ).TrimEnd( '\r', '\n', ' ', '\t' ), currentline + 1 );
                    ++currentline;
                }
            }
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
                return currentline >= Macro.Length ? null : Macro[currentline++];
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
                if (line.StartsWith( ":" )) continue;
                if (line.StartsWith( "END" )) //Terminate program
                {
                    string expr = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        expr = parsedLine[1]; //isolate expression
                    Environment.Exit( Int32.Parse( Evaluate( expr ) ) );
                }
                if (line.StartsWith( "IFRETURNISNOT" )) //conditional execution based on last return
                {
                    string value = "";
                    string expr = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        expr = parsedLine[1]; //isolate expression
                    if (parsedLine[2] != null)
                        value = parsedLine[2]; //isolate target value
                    value = Evaluate( value );
                    expr = Evaluate( expr );

                    if (value == expr) //last return matches value
                        continue; //do nothing, go to read next command
                                  //value is not equal to last response, execute conditional command
                    line = ""; //reassemble rest of conditional command
                    for (int i = 3 ; i < parsedLine.Length ; i++)
                    {
                        line += parsedLine[i];
                        if (i < parsedLine.Length - 1) line += ",";
                    }
                    //continue execution as if it was non-conditional
                }
                if (line.StartsWith( "IFRETURNIS" )) //conditional execution based on last return
                {
                    string value = "";
                    string expr = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        expr = parsedLine[1]; //isolate expression
                    if (parsedLine[2] != null)
                        value = parsedLine[2]; //isolate target value
                    value = Evaluate( value );
                    expr = Evaluate( expr );
                    if (value != Evaluate( expr )) //last return does not match value
                        continue; //do nothing, go to read next command
                                  //value is equal to last response
                    line = ""; //reassemble rest of command
                    for (int i = 3 ; i < parsedLine.Length ; i++)
                    {
                        line += parsedLine[i];
                        if (i < parsedLine.Length - 1) line += ",";
                    }
                    //continue execution as if it was non-conditional
                }
                if (line.StartsWith( "EVALUATE" )) //Set response to evaluation of expression
                {
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = parsedLine[1]; //isolate target value

                    response = Evaluate( parsedLine[1] );
                    changeVar( "response", response );
                    continue;

                }
                if (line.StartsWith( "SET" )) //set value of global var; create it if needed
                {
                    string variable = "";
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        variable = parsedLine[1];
                    if (parsedLine[2] != null)
                        value = Evaluate( parsedLine[2] );
                    if (!variables.ContainsKey( variable ))
                        AddVar( variable, null );
                    changeVar( variable, value );
                    continue;
                }
                if (line.StartsWith( "EXIT" )) //stop macro
                {
                    break;
                }
                if (line.StartsWith( "EXECUTE" )) //stop macro
                {
                    string value = "";

                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = parsedLine[1];
                    System.Diagnostics.Process.Start( "CMD.exe", "/C " + value );
                    continue;
                }

                if (line.StartsWith( "LOGERROR" )) //write log entry
                {
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = ExpandVariables( parsedLine[1] );

                    _logger.Error( value );
                    continue;
                }
                if (line.StartsWith( "GOTO" ))
                {
                    string value = "";
                    string[] line1 = line.Split( '#' ); //Disregard comments
                    string[] parsedLine = line1[0].Split( ',' );
                    if (string.IsNullOrWhiteSpace( parsedLine[0] )) //Disregard blanks lines
                        continue;
                    if (parsedLine[1] != null)
                        value = parsedLine[1].TrimEnd( '\r', '\n', ' ', '\t' );
                    if (!label.ContainsKey( value ))
                        _logger.Error( "Unknown label " + value );
                    else
                    {

                        currentline = label[value];
                        continue;
                    }

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
                    int startCommand = lin2[0].IndexOf( ":" ) + 1;
                    string command = lin2[0].Substring( startCommand );
                    target.SendCommand( command );
                    controller.parent.updateStatus( lin3[0], command );
                }

            }
            fs.Close();
        }


    }

}
