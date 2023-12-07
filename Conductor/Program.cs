using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http.Headers;
using System.Threading;
using System.Runtime.Remoting.Messaging;
using System.Diagnostics.Contracts;
using System.IO;
using Conductor;


namespace Conductor
{
    internal static class Program
    {


        /// <summary>
        /// The main entry point for the application.
        /// </summary> 

        static void Main()
        {
            // Waits here for the process to exit.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault( false );
            Application.Run( new Form1() );
        }
    }
}


