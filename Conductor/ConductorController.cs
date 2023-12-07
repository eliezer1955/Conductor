using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Conductor
{

    public class ConductorController
    {
        public struct CommandStructure
        {
            public byte CmdNumber;
            public string CmdName;
            public string Description;
            public string parameters;
            public string returns;
            public int timeout;
            public int response;
        }
        public bool RunLoop { get; set; }
        private int _receivedItemsCount = 0;                        // Counter of number of plain text items received
        private int _receivedBytesCount = 0;                        // Counter of number of plain text bytes received
        long _beginTime = 0;                                        // Start time, 1st item of sequence received 
        long _endTime = 0;                                          // End time, last item of sequence received 
        public SerialPort _serialPort = null;
        public Dictionary<string, int> CommandNumber = new Dictionary<string, int>();
        public ConductorClassLib rc = null;
        //public AsynchronousSocketListener listener = null;
        public MacroRunner macroRunner;
        public string CurrentMacro;
        public Form1 parent;

        public ConductorController( string runthis, Form1 parentIn )
        {
            parent = parentIn;
            CurrentMacro = runthis;
            //listener = new AsynchronousSocketListener( this );
        }



        private delegate void SetControlPropertyThreadSafeDelegate(
                         System.Windows.Forms.Control control,
                         string propertyName,
                         object propertyValue );

        public void SetControlPropertyThreadSafe(
            Control control,
            string propertyName,
            object propertyValue )
        {
            if (control.InvokeRequired)
            {
                control.Invoke( new SetControlPropertyThreadSafeDelegate
                ( SetControlPropertyThreadSafe ),
                new object[] { control, propertyName, propertyValue } );
            }
            else
            {
                control.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.SetProperty,
                    null,
                    control,
                    new object[] { propertyValue } );
            }
        }



        public Dictionary<string, string> roster = new Dictionary<string, string>();
        public Dictionary<string, musician> orchestra = new Dictionary<string, musician>();

        public int setupOrchestra( String partiture )
        {
            string basedir = "C:\\Users\\rosengau\\source\\repos\\";
            roster["PumpValve"] = "PumpValveDiagWF1\\PumpValveDiagWF\\bin\\x64\\debug\\PumpValveDiagWF.exe";
            roster["Stepper"] = "StepperWF1\\StepperWf\\bin\\debug\\StepperWf.exe";
            roster["roboClaw"] = "roboClawWF1\\bin\\x64\\debug\\WindowsApplication.exe";
            roster["RFID1"] = "RFIDWF";
            roster["RFID2"] = "RFIDWF";
            List<musician> ensemble = new List<musician>();


            const Int32 BufferSize = 128;
            using (var fileStream = File.OpenRead( partiture ))
            using (var streamReader = new StreamReader( fileStream, Encoding.UTF8, true, BufferSize ))
            {
                String line;
                while ((line = streamReader.ReadLine()) != null)
                {
                    //figure out which components will be needed for this macro (partiture)
                    var player = line.Split( ':' );
                    if (player.Length > 0)
                    {
                        if (roster.ContainsKey( player[0] ) & !orchestra.ContainsKey( player[0] ))
                        {
                            //instantiate each component
                            var m = new musician( player[0], basedir + roster[player[0]], this );
                            orchestra[player[0]] = m;
                            ensemble.Add( m );
                            DataGridViewRow row = (DataGridViewRow)parent.dataGridView1.Rows[0].Clone();
                            row.Cells[0].Value = player[0];
                            row.Cells[1].Value = "Unavailable";
                            parent.dataGridView1.Rows.Add( row );
                            parent.gridrow[player[0]] = parent.dataGridView1.Rows.Count - 2;
                            parent.Refresh();
                        }

                    }
                }
            }

            foreach (musician player in ensemble)
            {
                //start each component executable
                //Each executable will initiate connect attempt
                // only start executable is musician is not already active
                player.Start( this );
            }
            //wait for all musicians to connect
            bool done = false;
            do
            {
                done = true;
                foreach (musician player in ensemble)
                {
                    if (player.name == null)
                    {
                        done = false;
                        break;
                    }

                }

            } while (!done);
            Console.WriteLine( "All components required are connected" );
            /*
            //Fire up receiving threads, one per musician
            foreach (var player in orchestra.Values)
            {
                player.receiver = new Thread( player.receive );
                player.receiver.Start();
            }*/
            return 0;
        }

    }
}