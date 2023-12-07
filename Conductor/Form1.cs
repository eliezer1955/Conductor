using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace Conductor
{
    public partial class Form1 : Form
    {
        string CurrentMacro = "Partiture.txt";
        public ConductorController cc;
        public Dictionary<string, int> gridrow = new Dictionary<string, int>();
        public Form1()
        {
            InitializeComponent();
            button2.Text = CurrentMacro;

        }

        private void Form1_Load( object sender, EventArgs e )
        {
            cc = new ConductorController( CurrentMacro, this );
        }

        // run macro
        private void button1_Click( object sender, EventArgs e )
        {
            Control[] macro = this.Controls.Find( "button2", true );
            string CurrentMacro = macro[0].Text;
            cc.setupOrchestra( CurrentMacro );
            MacroRunner macroRunner = new MacroRunner( cc, CurrentMacro );
            Thread macroThread = new Thread( () => macroRunner.RunMacro() );
            macroThread.Name = "Macro Runner";
            macroThread.Start();
            //macroRunner.RunMacro();
        }

        // Select macro
        private void button2_Click( object sender, EventArgs e )
        {
            var picker = new OpenFileDialog();
            picker.FileName = CurrentMacro;
            picker.DefaultExt = "txt";
            picker.InitialDirectory = Environment.CurrentDirectory;
            picker.Filter = "txt files (*.txt)|*.txt";
            if (picker.ShowDialog() == DialogResult.OK)
            {
                CurrentMacro = picker.FileName;
                button2.Text = CurrentMacro;

            }
        }

        private void dataGridView1_CellContentClick( object sender, DataGridViewCellEventArgs e )
        {

        }

        public void updateStatus(String component, String status)
        {
            try
            {
                var row = gridrow[component];
                dataGridView1[1, row].Value = status;

                // this may be called from non-GUI thread, thus
                this.Invoke( (MethodInvoker)delegate { this.Invalidate(); } );
                this.Invoke( (MethodInvoker)delegate { this.Refresh(); } );
                Thread.Yield();
            }
            catch(Exception ex) { };
        }
    }
}
