using Communication.Core;
using NamedPipes.Core;
using NamedPipes.Server;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace Conductor
{
    public class PipeServer
    {
        musician parent;
        byte[] buff = new byte[1024];
        StringBuilder sb = new StringBuilder();
        UnicodeEncoding streamEncoding = new UnicodeEncoding();
        public PipeServer( musician musicianin )
        {
            parent = musicianin;
        }

        public async void ServerThread( object data )
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            string musicianName;
            SemaphoreSlim semaphore=new SemaphoreSlim(0,1);

            IServer server = new NamedPipeServer( parent.pipeName );
            server.ServerStarted += ( _, args ) =>
                Console.WriteLine( "SERVER => Server started." );
            server.ClientConnected += ( _, args ) =>
            {
                Console.WriteLine( "SERVER => A client connected." );
                semaphore.Release();
            };
            server.MessageReceived += ( _, args ) =>
            {
                Console.WriteLine( $"SERVER => Message received from client: {(args as MessageReceivedEventArgs).Message}" );
                if (parent.name == null)
                {
                    var message = (args as MessageReceivedEventArgs).Message;
                    if (!message.StartsWith( "\0" ))
                    {
                        musicianName = parent.name = message;
                        parent.name = musicianName;
                        parent.controller.orchestra[musicianName] = parent;
                        parent.controller.parent.updateStatus( musicianName, "Connected" );
                        Console.WriteLine( "Component {0} connected on thread {1}", musicianName, threadId );
                    }
                }
                else
                {
                    musicianName = parent.name;
                    var message = (args as MessageReceivedEventArgs).Message;
                    parent.controller.parent.updateStatus( musicianName, message );
                }
            };
            server.Disconnected += ( _, args ) =>
               Console.WriteLine( $"SERVER => A client disconnected." );
            
            await server.Start();
            semaphore.Wait();
            parent.cmdToSend= "\0";

            try
            {
                while (true)
                {

                    if (parent.cmdToSend != null)
                    {
                        Console.WriteLine( "Sending {0} to {1} ", parent.cmdToSend, parent.name );
                        await server.Send( parent.cmdToSend );
                        lock (parent._writerSemaphore)
                        {
                            parent.cmdToSend = null;
                        }
                    }
                    Thread.Yield();
                }

            }
            // Catch the IOException that is raised if the pipe is broken
            // or disconnected.
            catch (IOException e)
            {
                Console.WriteLine( "ERROR: {0}", e.Message );
            }
        }
    }
}

    