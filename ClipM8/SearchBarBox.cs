using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipM8
{
    public partial class SearchBarBox : UserControl
    {
        // Delegato personalizzato per l'evento di ricerca
        public delegate void SearchRequestedHandler(object sender, string searchText);

        // Eventi pubblici
        public event SearchRequestedHandler SearchRequested;
        public event EventHandler SearchNextRequested;

        // Proprietà: restituisce lo stato del checkbox "Case Sensitive"
        public bool CaseSensitive
        {
            get { return chkCaseSensitive.Checked; }
        }

        public SearchBarBox()
        {
            InitializeComponent();
        }

        // Metodo per forzare il focus nella textbox
        public void FocusTextBox()
        {
            textBoxSearch.Focus();
        }

        private void btnFind_Click(object sender, EventArgs e)
        {
            if (SearchRequested != null)
                SearchRequested(this, textBoxSearch.Text);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            if (SearchNextRequested != null)
                SearchNextRequested(this, EventArgs.Empty);
        }
    }
}
