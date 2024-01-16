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
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger( System.Reflection.MethodBase.GetCurrentMethod().DeclaringType );



        /// <summary>
        /// The main entry point for the application.
        /// </summary> 

        static void Main()
        {

            var configFile = new FileInfo( Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "log4net.config" ) );
            log4net.Config.XmlConfigurator.Configure( configFile );
            log.Info( "PumpValveDiag starting!" );

            // Waits here for the process to exit.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault( false );
            Application.Run( new Form1() );
        }
    }
}


