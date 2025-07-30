using System;
using System.Windows.Forms;

namespace ClipM8
{
    static class Program
    {
        /// <summary>
        /// Punto di ingresso principale dell'applicazione.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore imprevisto durante l'avvio dell'applicazione: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
