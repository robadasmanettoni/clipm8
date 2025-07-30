using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClipM8
{
    public class PreviewImageBox : UserControl
    {
        public enum ImageViewMode { Scale, Stretch, Normal }

        private ToolStrip toolStrip;
        private ToolStripButton btnScale, btnStretch, btnOriginal;
        private ToolStripButton btnZoomIn, btnZoomOut, btnZoomReset;

        private Panel scrollPanel;
        private PictureBox pictureBox;

        private float zoomFactor = 1.0f;

        public event EventHandler ScaleRequested;
        public event EventHandler StretchRequested;
        public event EventHandler NormalSizeRequested;
        public event EventHandler ZoomInRequested;
        public event EventHandler ZoomOutRequested;
        public event EventHandler ZoomResetRequested;

        private ImageViewMode currentMode = ImageViewMode.Normal;

        public PreviewImageBox()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Toolstrip
            toolStrip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };

            // Pulsanti modalità
            btnScale = new ToolStripButton("Scala") { CheckOnClick = true };
            btnStretch = new ToolStripButton("Stira") { CheckOnClick = true };
            btnOriginal = new ToolStripButton("Normale") { CheckOnClick = true };

            // Pulsanti zoom
            btnZoomIn = new ToolStripButton("+");
            btnZoomOut = new ToolStripButton("-");
            btnZoomReset = new ToolStripButton("100%");

            // Associazione eventi ai pulsanti
            btnScale.Click += (s, e) =>
            {
                UncheckAllViewModeButtons();
                btnScale.Checked = true;
                if (ScaleRequested != null)
                    ScaleRequested(this, EventArgs.Empty);
            };

            btnStretch.Click += (s, e) =>
            {
                UncheckAllViewModeButtons();
                btnStretch.Checked = true;
                if (StretchRequested != null)
                    StretchRequested(this, EventArgs.Empty);
            };

            btnOriginal.Click += (s, e) =>
            {
                UncheckAllViewModeButtons();
                btnOriginal.Checked = true;
                if (NormalSizeRequested != null)
                    NormalSizeRequested(this, EventArgs.Empty);
            };

            btnZoomIn.Click += (s, e) =>
            {
                if (ZoomInRequested != null)
                    ZoomInRequested(this, EventArgs.Empty);
            };

            btnZoomOut.Click += (s, e) =>
            {
                if (ZoomOutRequested != null)
                    ZoomOutRequested(this, EventArgs.Empty);
            };

            btnZoomReset.Click += (s, e) =>
            {
                if (ZoomResetRequested != null)
                    ZoomResetRequested(this, EventArgs.Empty);
            };

            toolStrip.Items.AddRange(new ToolStripItem[] {
                btnScale, btnStretch, btnOriginal,
                new ToolStripSeparator(),
                btnZoomOut, btnZoomReset, btnZoomIn
            });

            // PictureBox
            pictureBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Normal,
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };

            // Pannello scrollabile
            scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.White
            };

            scrollPanel.Controls.Add(pictureBox);

            // Aggiunge i controlli al contenitore principale
            this.Controls.Add(scrollPanel);
            this.Controls.Add(toolStrip);
        }

        // Deseleziona i pulsanti Scala, Stira e Normale della toolbar
        private void UncheckAllViewModeButtons()
        {
            btnScale.Checked = false;
            btnStretch.Checked = false;
            btnOriginal.Checked = false;
        }

        // Imposta una nuova immagine nel controllo
        public void SetImage(Image image)
        {
            pictureBox.Image = image;

            if (image != null)
            {
                if (currentMode == ImageViewMode.Normal)
                {
                    zoomFactor = 1.0f;
                    ApplyZoom();
                }
                else
                {
                    pictureBox.Dock = DockStyle.Fill;
                }
            }
        }

        // Imposta la modalità di visualizzazione corrente
        public void SetViewMode(ImageViewMode mode)
        {
            currentMode = mode;

            // Aggiorna stato pulsanti
            btnScale.Checked = mode == ImageViewMode.Scale;
            btnStretch.Checked = mode == ImageViewMode.Stretch;
            btnOriginal.Checked = mode == ImageViewMode.Normal;

            switch (mode)
            {
                case ImageViewMode.Scale:
                    // Modalità "Scala": l'immagine viene scalata per riempire il PictureBox mantenendo le proporzioni.
                    pictureBox.Dock = DockStyle.Fill;              // Il PictureBox si estende per riempire il pannello.
                    pictureBox.SizeMode = PictureBoxSizeMode.Zoom; // L'immagine si adatta allo spazio mantenendo il rapporto di aspetto.
                    break;

                case ImageViewMode.Stretch:
                    // Modalità "Stira": l'immagine viene forzatamente deformata per riempire il PictureBox.
                    pictureBox.Dock = DockStyle.Fill;                      // Come sopra, il PictureBox occupa tutto il pannello.
                    pictureBox.SizeMode = PictureBoxSizeMode.StretchImage; // L'immagine viene adattata a forza (deformata se necessario).
                    break;

                case ImageViewMode.Normal:
                    // Modalità "Normale": l'immagine viene visualizzata alla sua dimensione originale.
                    pictureBox.Dock = DockStyle.None;                // Il PictureBox non si adatta al pannello, ma mantiene una posizione fissa.
                    pictureBox.SizeMode = PictureBoxSizeMode.Normal; // L'immagine viene disegnata alla sua dimensione nativa, senza ridimensionamento.

                    if (pictureBox.Image != null)
                    {
                        zoomFactor = 1.0f; // Reset esplicito dello zoom
                        ApplyZoom();       // Forza il ridisegno per aggiornare la visualizzazione.
                    }                                             
                                                                  
                    ApplyZoom(); // Applica un eventuale fattore di zoom (ingrandimento o riduzione) in modalità normale.
                    break;
            }

        }

        // Proprietà pubblica per ottenere o impostare la modalità
        public ImageViewMode ViewMode
        {
            get { return currentMode; }
            set
            {
                currentMode = value;
                UpdateImageDisplayMode();
            }
        }

        // Proprietà pubblica per ottenere o impostare l'immagine
        public Image Image
        {
            get { return pictureBox.Image; }
            set { pictureBox.Image = value; }
        }

        // Applica le impostazioni di visualizzazione secondo la modalità corrente
        private void UpdateImageDisplayMode()
        {
            switch (currentMode)
            {
                case ImageViewMode.Scale:
                    pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
                    pictureBox.Dock = DockStyle.Fill;
                    break;
                case ImageViewMode.Stretch:
                    pictureBox.SizeMode = PictureBoxSizeMode.StretchImage;
                    pictureBox.Dock = DockStyle.Fill;
                    break;
                case ImageViewMode.Normal:
                default:
                    pictureBox.SizeMode = PictureBoxSizeMode.Normal;
                    pictureBox.Dock = DockStyle.None;

                    ApplyZoom(); // Applicazione zoom solo in modalità normale
                    break;
            }
        }

        // Aumenta lo zoom
        public void ZoomIn()
        {
            Zoom += 0.1f;
            UpdateImageLayout();
        }

        // Riduce lo zoom
        public void ZoomOut()
        {
            Zoom -= 0.1f;
            UpdateImageLayout();
        }

        // Ripristina lo zoom al 100%
        public void ResetZoom()
        {
            Zoom = 1.0f;
            UpdateImageLayout();
        }

        // Proprietà per leggere/scrivere il livello dello zoom
        public float Zoom
        {
            get { return zoomFactor; }
            set
            {
                if (value < 0.1f) value = 0.1f;
                if (value > 10.0f) value = 10.0f;
                zoomFactor = value;
                ApplyZoom();
            }
        }

        // Applica il fattore di zoom all'immagine in modalità normale
        private void ApplyZoom()
        {
            if (pictureBox.Image != null && currentMode == ImageViewMode.Normal)
            {
                int w = (int)(pictureBox.Image.Width * zoomFactor);
                int h = (int)(pictureBox.Image.Height * zoomFactor);

                pictureBox.Size = new Size(w, h);
                pictureBox.Location = new Point(
                    Math.Max((scrollPanel.ClientSize.Width - pictureBox.Width) / 2, 0),
                    Math.Max((scrollPanel.ClientSize.Height - pictureBox.Height) / 2, 0)
                );
            }
        }

        // Aggiorna layout dell'immagine quando lo zoom cambia
        private void UpdateImageLayout()
        {
            if (pictureBox.Image == null)
                return;

            int imgW = (int)(pictureBox.Image.Width * zoomFactor);
            int imgH = (int)(pictureBox.Image.Height * zoomFactor);

            pictureBox.Size = new Size(imgW, imgH);

            // Centra l'immagine se è più piccola del pannello
            int x = Math.Max((scrollPanel.ClientSize.Width - imgW) / 2, 0);
            int y = Math.Max((scrollPanel.ClientSize.Height - imgH) / 2, 0);
            pictureBox.Location = new Point(x, y);

            // Aggiunta fondamentale per scalare anche l'immagine
            if (currentMode == ImageViewMode.Normal)
                pictureBox.SizeMode = PictureBoxSizeMode.StretchImage;
        }
    }
}
