using System.Diagnostics;
using System.IO;
using System;
using System.Linq;
using static System.Windows.Forms.AxHost;
using System.Text;
using System.Threading;
using Microsoft.SqlServer.Server;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace Conductor
{
    public class musician
    {
        string Filename;
        System.Diagnostics.Process process;
        string[] CommandLineargs;
        public String lastCommand = null;
        public String lastResponse = null;
        public ConductorController controller = null;
        public Thread receiver = null;
        public bool stopReader = false;
        //public StreamString stream = null;
        PipeServer pipeServer = null;
        public string name;
        public string pipeName;
        public StreamWriter sw = null;
        public StreamReader sr = null;
        public readonly object _writerSemaphore = new object();
        public string cmdToSend = null;

        public musician( string pipeName, string executable, ConductorController controller )
        {
            this.pipeName = pipeName;
            Filename = executable;
            pipeServer = new PipeServer( this );
            receiver = new Thread( pipeServer.ServerThread );
            receiver.Name = pipeName+"Receiver";
            this.controller = controller;
            receiver.Start();
        }
        public void Start( ConductorController cc )
        {
            controller = cc;
            //Don't start external executable if stream is already connected
            if (sw != null) return;
            process = new System.Diagnostics.Process();
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName( Filename );
            process.StartInfo.FileName = Filename;
            process.StartInfo.Arguments = "11000";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Maximized;
            process.Start();
        }

        public async void SendCommand( String Command )
        {
            lastCommand = Command;
            var local = this.name;
            Console.WriteLine( "{0} command:{1}", local, Command );
            //await _writerSemaphore.WaitAsync();
            //AsynchronousSocketListener.Send( stateObject.workSocket, lastCommand );
            //stateObject.workSocket.Send( Encoding.UTF8.GetBytes( lastCommand ) );
            lock (_writerSemaphore)
            {
                //await sw.WriteLineAsync( lastCommand );
                this.cmdToSend = lastCommand;
            }
            while (this.cmdToSend != null)
            {
                Thread.Yield();
            }
            //sw.WriteLine( lastCommand );
            //_writerSemaphore.Release();
        }


    }
}