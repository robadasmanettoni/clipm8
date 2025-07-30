using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClipM8
{
    public class PreviewRichTextBox : UserControl
    {
        private Panel lineNumberPanel;
        private RichTextBox richTextBox;
        private ToolStrip toolStrip;
        private ToolStripButton btnWordWrap;
        private ToolStripButton btnLineNumbers;
        private bool showLineNumbers = true;
        private Font monoFont = new Font("Courier New", 9);

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, ref Point lParam);
        private const int EM_GETSCROLLPOS = 0x0400 + 221;

        public PreviewRichTextBox()
        {
            // === TOOLBAR ===
            toolStrip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
            btnWordWrap = new ToolStripButton("WordWrap") { CheckOnClick = true };
            btnLineNumbers = new ToolStripButton("Linee") { CheckOnClick = true };

            btnWordWrap.Click += (s, e) => { WordWrap = btnWordWrap.Checked; };
            btnLineNumbers.Click += (s, e) => { ShowLineNumbers = btnLineNumbers.Checked; };

            toolStrip.Items.AddRange(new ToolStripItem[] { btnWordWrap, btnLineNumbers });

            // === LINE NUMBER PANEL ===
            lineNumberPanel = new Panel
            {
                Dock = DockStyle.Left,
                BackColor = Color.LightGray
            };

            // === RICHTEXTBOX ===
            richTextBox = new RichTextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = monoFont,
                WordWrap = false,
                ReadOnly = true,
                TabStop = false
            };

            // === EVENTI ===
            richTextBox.VScroll += (s, e) => lineNumberPanel.Invalidate();
            richTextBox.TextChanged += (s, e) => lineNumberPanel.Invalidate();
            richTextBox.Resize += (s, e) => lineNumberPanel.Invalidate();
            richTextBox.FontChanged += (s, e) => lineNumberPanel.Invalidate();
            richTextBox.SelectionChanged += (s, e) => lineNumberPanel.Invalidate();
            lineNumberPanel.Paint += LineNumberPanel_Paint;

            // === ASSEMBLA ===
            this.Controls.Add(richTextBox);
            this.Controls.Add(lineNumberPanel);
            this.Controls.Add(toolStrip);
        }

        private void LineNumberPanel_Paint(object sender, PaintEventArgs e)
        {
            if (!showLineNumbers) return;

            Point scrollPoint = new Point();
            SendMessage(richTextBox.Handle, EM_GETSCROLLPOS, 0, ref scrollPoint);
            int scrollY = scrollPoint.Y;

            int lineHeight = TextRenderer.MeasureText("A", monoFont).Height;
            int firstLine = scrollY / lineHeight;
            int visibleLines = this.Height / lineHeight + 1;
            int maxLineNumber = richTextBox.Lines.Length;
            int maxDigits = maxLineNumber.ToString().Length;

            int marginWidth = TextRenderer.MeasureText(new string('9', maxDigits), monoFont).Width + 8;
            lineNumberPanel.Width = marginWidth;

            for (int i = 0; i < visibleLines; i++)
            {
                int y = i * lineHeight - (scrollY % lineHeight);
                int lineNumber = firstLine + i + 1;
                if (lineNumber > maxLineNumber) break;

                string lineStr = lineNumber.ToString();
                SizeF size = e.Graphics.MeasureString(lineStr, monoFont);
                float x = marginWidth - size.Width - 4;

                int selStart = richTextBox.SelectionStart;
                int currentLine = richTextBox.GetLineFromCharIndex(selStart);
                if (lineNumber - 1 == currentLine)
                {
                    e.Graphics.FillRectangle(Brushes.LightBlue, 0, y, marginWidth, lineHeight);
                }

                e.Graphics.DrawString(lineStr, monoFont, Brushes.Black, x, y);
            }
        }

        // === Proprietà pubbliche ===

        public RichTextBox InnerRichTextBox
        {
            get { return richTextBox; }
        }

        public bool ReadOnly
        {
            get { return richTextBox.ReadOnly; }
            set { richTextBox.ReadOnly = value; }
        }

        public bool ShowLineNumbers
        {
            get { return showLineNumbers; }
            set
            {
                showLineNumbers = value;
                lineNumberPanel.Visible = value;
                lineNumberPanel.Invalidate();
                if (btnLineNumbers != null) btnLineNumbers.Checked = value;
            }
        }

        public bool WordWrap
        {
            get { return richTextBox.WordWrap; }
            set
            {
                richTextBox.WordWrap = value;
                if (btnWordWrap != null) btnWordWrap.Checked = value;
            }
        }

        public string Rtf
        {
            get { return richTextBox.Rtf; }
            set { richTextBox.Rtf = value; }
        }

        public string TextContent
        {
            get { return richTextBox.Text; }
            set { richTextBox.Text = value; }
        }

        public class CursorPosition
        {
            public int Line { get; set; }
            public int Column { get; set; }

            public CursorPosition(int line, int column)
            {
                this.Line = line;
                this.Column = column;
            }
        }

        public CursorPosition GetCursorPosition()
        {
            int index = richTextBox.SelectionStart;
            int line = richTextBox.GetLineFromCharIndex(index) + 1;
            int col = index - richTextBox.GetFirstCharIndexOfCurrentLine() + 1;
            return new CursorPosition(line, col);
        }

        public override string ToString()
        {
            return richTextBox.Text;
        }
    }
}
