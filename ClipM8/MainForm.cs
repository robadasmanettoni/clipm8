using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Data.SQLite;

namespace ClipM8
{
    public partial class MainForm : Form
    {
        // === 1. Variabili di istanza ===
        // Campi relativi all’interfaccia
        private ListViewColumnSorter sorter;
        private Timer clipboardTimer;

        // Campi per i dati in memoria
        private List<ClipItem> allClips = new List<ClipItem>();

        // Costanti ===
        private const int MaxContentSizeBytes = 5 * 1024 * 1024;

        // Contenuto attuale della clipboard
        private byte[] lastClipboardContent = null;

        // Dati per la stampa
        private byte[] printContent;
        private string printType;

        // === 2. Costruttore e Load ===
        public MainForm()
        {
            // Inizializza tutti i componenti grafici della form (generato dal Designer)
            InitializeComponent();

            // Collego gli eventi del form
            this.Load += MainForm_Load; 
            this.FormClosing += MainForm_FormClosing;
            this.FormClosed += MainForm_FormClosed;

            // Associa l'evento SelectionChanged dell'editor RTF al metodo che aggiorna la posizione del cursore nello status bar
            previewRichTextBox.InnerRichTextBox.SelectionChanged += UpdateCursorPositionInStatusBar;

            // TreeView: quando si seleziona una cartella nel TreeView, aggiorna la ListView con le clip corrispondenti
            treeViewFolders.AfterSelect += treeViewFolders_AfterSelect;
            treeViewFolders.AfterLabelEdit += treeViewFolders_AfterLabelEdit;

            // Toolbar/Menu: aggiornamento dinamico della voce "Modifica"
            toolStripEditMenu.DropDownOpening += toolStripEditMenu_DropDownOpening;

            // Drag & Drop: supporto allo spostamento delle clip tra cartelle nel TreeView
            treeViewFolders.DragEnter += treeViewFolders_DragEnter;
            treeViewFolders.DragDrop += treeViewFolders_DragDrop;
            treeViewFolders.DragOver += treeViewFolders_DragOver;

            // PreviewImageBox: associa gli eventi dell’anteprima immagine
            previewImageBox.ScaleRequested += (s, e) => toolStripViewMenuPreviewScaleImage.PerformClick();
            previewImageBox.StretchRequested += (s, e) => toolStripViewMenuPreviewStretchImage.PerformClick();
            previewImageBox.NormalSizeRequested += (s, e) => toolStripViewMenuPreviewOriginalSize.PerformClick();
            previewImageBox.ZoomInRequested += (s, e) => toolStripViewMenuPreviewZoomIn.PerformClick();
            previewImageBox.ZoomOutRequested += (s, e) => toolStripViewMenuPreviewZoomOut.PerformClick();
            previewImageBox.ZoomResetRequested += (s, e) => toolStripViewMenuPreviewZoomReset.PerformClick();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // === 1. Inizializza struttura dati e DB ===
            SettingsDB.InitializeDatabaseIfMissing();    // Crea il database (se non esiste) e le relative tabelle di supporto

            // === 2. Applica impostazioni dell'interfaccia e carica la struttura e contenuti dell'app ===
            InitializeUISettings();           // Crea il database (se non esiste) e le relative tabelle di supporto
            InitializeTreeView();             // Carica la struttura delle cartelle (TreeView) dalla tabella folders
            InitializeListView();             // Inizializza la visualizzazione delle clip (ListView) con le colonne
            RefreshListViewForSelectedNode(); // Popola la ListView con le clip della cartella attualmente selezionata nel TreeView
            InitializeSearchBar();

            // Avvio il monitoraggio della clipboard (se attivo nelle impostazioni)
            bool clipboardMonitor = bool.Parse(SettingsDB.Load("clipboard_monitor", "true"));
            toolStripToolsMenuCaptureClips.Checked = clipboardMonitor;
            if (clipboardMonitor)
                StartClipboardMonitor();  // Avvia il timer che controlla la clipboard
        }

        // === 3. Inizializzazioni UI e DB ===
        private void InitializeUISettings()
        {
            try
            {
                // 1. Posizione e dimensione finestra
                int left = int.Parse(SettingsDB.Load("window_left"));
                int top = int.Parse(SettingsDB.Load("window_top"));
                int width = int.Parse(SettingsDB.Load("window_width"));
                int height = int.Parse(SettingsDB.Load("window_height"));

                this.StartPosition = FormStartPosition.Manual;
                this.Location = new Point(left, top);
                this.Size = new Size(width, height);

                // 2. WordWrap nell'editor
                bool wordWrap = bool.Parse(SettingsDB.Load("editor_wordwrap", "false"));
                toolStripViewMenuPreviewWordwrap.Checked = wordWrap;

                // 3. Numeri di riga nell'editor
                bool lineNumbers = bool.Parse(SettingsDB.Load("editor_linenumbers", "true"));
                toolStripViewMenuPreviewLineNumbers.Checked = lineNumbers;

                // 4. Visibilità del riquadro anteprima
                bool boxPreviewVisible = bool.Parse(SettingsDB.Load("view_boxpreview", "true"));
                splitContainer1.Panel2Collapsed = !boxPreviewVisible;
                toolStripViewMenuPreviewPanel.Checked = boxPreviewVisible;

                // 5. Griglia nella ListView
                bool grid = bool.Parse(SettingsDB.Load("listview_grid", "true"));
                listViewClips.GridLines = grid;
                toolStripViewMenuClipBrowserGrid.Checked = grid;

                // 6. Righe alternate nella ListView
                bool altRows = bool.Parse(SettingsDB.Load("listview_rows", "false"));
                toolStripViewMenuClipBrowserAltRow.Checked = altRows;

                // 7. Modalità di visualizzazione della ListView
                string viewListMode = SettingsDB.Load("listview_viewmode", "details").ToLower();
                switch (viewListMode)
                {
                    case "largeicon":
                        listViewClips.View = View.LargeIcon;
                        toolStripViewMenuClipBrowserLargeIcons.Checked = true;
                        break;

                    case "list":
                        listViewClips.View = View.List;
                        toolStripViewMenuClipBrowserList.Checked = true;
                        break;

                    case "tile":
                        listViewClips.View = View.Tile;
                        toolStripViewMenuClipBrowserTiles.Checked = true;
                        break;

                    default:
                        listViewClips.View = View.Details;
                        toolStripViewMenuClipBrowserDetails.Checked = true;
                        break;
                }

                // 8. Visibilità della toolbar
                bool toolbarVisible = bool.Parse(SettingsDB.Load("view_toolbar", "true"));
                toolStripViewMenuToolbar.Checked = toolbarVisible;
                toolStrip1.Visible = toolbarVisible;

                // 9. Visibilità della status bar
                bool statusbarVisible = bool.Parse(SettingsDB.Load("view_statusbar", "true"));
                toolStripViewMenuStatusbar.Checked = statusbarVisible;
                statusStrip1.Visible = statusbarVisible;

                // 10. Modalità visualizzazione immagini
                string viewImgMode = SettingsDB.Load("image_view_mode", "normal").ToLower();
                switch (viewImgMode)
                {
                    case "scale":
                        previewImageBox.ViewMode = PreviewImageBox.ImageViewMode.Scale;
                        toolStripViewMenuPreviewScaleImage.Checked = true;
                        break;

                    case "stretch":
                        previewImageBox.ViewMode = PreviewImageBox.ImageViewMode.Stretch;
                        toolStripViewMenuPreviewStretchImage.Checked = true;
                        break;

                    default:
                        previewImageBox.ViewMode = PreviewImageBox.ImageViewMode.Normal;
                        toolStripViewMenuPreviewOriginalSize.Checked = true;
                        break;
                }

                // 11. Zoom immagine
                int imageZoom = int.Parse(SettingsDB.Load("image_zoom_img", "100"));
                // SetImageZoom(imageZoom); // se disponibile

                // 12. Zoom testo RTF
                int textZoom = int.Parse(SettingsDB.Load("image_zoom_txt", "100"));
                // SetTextZoom(textZoom); // se disponibile

                // 15. Pulizia status bar
                toolStripStatusCollection.Text = "";
                toolStripStatusFormat.Text = "";
                toolStripStatusMIME.Text = "";
                toolStripStatusPosPreview.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento della struttura delle cartelle: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeTreeView()
        {
            treeViewFolders.Nodes.Clear();

            try
            {
                // Dati di contesto e connessione
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";

                // Strutture dati per uso interno
                List<FolderItem> folders = new List<FolderItem>();
                Dictionary<int, TreeNode> nodesById = new Dictionary<int, TreeNode>();
                TreeNode inboxNode = null;

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    string sql = "SELECT id, name, parent_id, image_key, is_locked, is_protected, sort_order FROM folders ORDER BY sort_order;";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            FolderItem folder = new FolderItem();
                            folder.Id = reader.GetInt32(0);
                            folder.Name = reader.GetString(1);
                            folder.ParentId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                            folder.ImageKey = reader.GetString(3);
                            folder.IsLocked = reader.GetInt32(4) == 1;
                            folders.Add(folder);
                        }
                    }

                    conn.Close();
                }

                // 1° passaggio: crea tutti i nodi e inseriscili nel dizionario
                foreach (FolderItem folder in folders)
                {
                    TreeNode node = new TreeNode(folder.Name);
                    node.Name = folder.Id.ToString();
                    node.Tag = folder;
                    node.ImageKey = folder.ImageKey;
                    node.SelectedImageKey = folder.ImageKey;

                    nodesById[folder.Id] = node;

                    if (folder.Id == 2) // InBox
                        inboxNode = node;
                }

                // 2° passaggio: collega tra loro i nodi padre e figli
                foreach (FolderItem folder in folders)
                {
                    TreeNode node = nodesById[folder.Id];

                    if (folder.ParentId == null)
                    {
                        treeViewFolders.Nodes.Add(node); // Nodo radice
                    }
                    else if (nodesById.ContainsKey(folder.ParentId.Value))
                    {
                        nodesById[folder.ParentId.Value].Nodes.Add(node); // Nodo figlio
                    }
                }

                treeViewFolders.ExpandAll();

                // Seleziona la cartella InBox all'avvio
                if (inboxNode != null)
                {
                    treeViewFolders.SelectedNode = inboxNode;
                    inboxNode.EnsureVisible();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle cartelle: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeListView()
        {
            // Pulisce eventuali colonne esistenti prima di aggiungerne di nuove
            listViewClips.Clear();

            // Aggiunge le colonne con intestazioni e larghezza fissa
            listViewClips.Columns.Add("Titolo", 200);         // Nome della clip
            listViewClips.Columns.Add("Data/Ora", 120);       // Data e ora della creazione
            listViewClips.Columns.Add("Tipo", 100);           // Tipo della clip (testo, link, immagine, ecc.)
            listViewClips.Columns.Add("Dimensione", 80);      // Dimensione
            listViewClips.Columns.Add("Cartella", 100);       // Cartella della clip
            listViewClips.Columns.Add("Origine", 130);        // Programma da cui proviene la clip
            listViewClips.Columns.Add("Preferiti", 70).TextAlign = HorizontalAlignment.Center;     // Indica che la clip è tra i preferiti
            listViewClips.Columns.Add("Bloccato", 70).TextAlign = HorizontalAlignment.Center;      // Indica che la clip è bloccata (non può essere eliminata)
            listViewClips.Columns.Add("ID", 60);              // ID interno della clip

            // Quando si seleziona una riga, seleziona tutte le celle (non solo la prima colonna)
            listViewClips.FullRowSelect = true;

            listViewClips.SmallImageList = imageList1;
            listViewClips.LargeImageList = imageList1;

            listViewClips.MultiSelect = true;
            listViewClips.AllowDrop = false;

            // Associa un gestore eventi per gestire il cambio di selezione
            listViewClips.SelectedIndexChanged += listViewClips_SelectedIndexChanged;
            listViewClips.ItemDrag += listViewClips_ItemDrag;

            // Gestione ordinamento delle colonne
            sorter = new ListViewColumnSorter();
            listViewClips.ListViewItemSorter = sorter;

            listViewClips.ColumnClick += (s, e) =>
            {
                if (e.Column == sorter.SortColumn)
                    sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
                else
                {
                    sorter.SortColumn = e.Column;
                    sorter.Order = SortOrder.Ascending;
                }

                listViewClips.Sort();
            };
        }

        private void RefreshListViewForSelectedNode()
        {
            // Pulisce la ListView
            listViewClips.Items.Clear();

            // Ricava l'ID della cartella selezionata nel TreeView
            int folderId;
            if (!int.TryParse(GetCurrentSelectedNodeTag(), out folderId)) return;

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    string sql = @"SELECT id, title, type, created_at, size_bytes, folder_id, source, image_key, is_favorite, is_locked 
                                   FROM clips WHERE folder_id = @folderId ORDER BY created_at DESC";

                    using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@folderId", folderId);
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                // === Estrae i dati dal database ===
                                string titolofull = reader["title"].ToString();
                                string primaRiga  = titolofull.Split(new[] { '\r', '\n' })[0];
                                string titolo     = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                                string data       = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                                string tipo       = reader["type"].ToString();
                                string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                                string cartella   = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                                string origine    = reader["source"].ToString();
                                string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                                string isLocked   = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                                string id         = reader["id"].ToString();

                                ListViewItem item = new ListViewItem(new string[]
                                {
                                    titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                                });

                                item.ImageKey = reader["image_key"].ToString();
                                listViewClips.Items.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Applica colori alternati se ci sono più clip
            if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
        }

        private void InitializeSearchBar()
        {
            searchBar = new SearchBarBox();
            searchBar.Dock = DockStyle.Top;
            searchBar.Visible = false;

            searchBar.SearchRequested += SearchBar_SearchRequested;

            this.Controls.Add(searchBar);
            this.Controls.SetChildIndex(searchBar, this.Controls.GetChildIndex(toolStrip1) - 1);
        }

        // === 3a. Eventi TreeView ===
        private void treeViewFolders_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node == null)
                return;

            string selectedId = e.Node.Name;

            switch (selectedId)
            {
                case "4":
                    LoadFavoriteClips();
                    return;
                case "7":
                    LoadTodayClips();
                    return;
                case "8":
                    LoadWeekClips();
                    return;
                case "9":
                    LoadMonthClips();
                    return;
                case "10":
                    LoadAllClips();
                    return;
                case "11":
                    LoadClipsByType("testo");
                    return;
                case "12":
                    LoadClipsByType("richtext");
                    return;
                case "13":
                    LoadClipsByType("immagine");
                    return;
                case "14":
                    LoadClipsByType("link");
                    return;
            }

            // Altrimenti usa la logica standard per cartelle normali
            RefreshListViewForSelectedNode();
        }

        // Parte il trascinamento da ListView
        private void listViewClips_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Crea una lista serializzabile di ListViewItem da trascinare
            var items = new List<ListViewItem>();

            foreach (ListViewItem item in listViewClips.SelectedItems)
            {
                items.Add(item);
            }

            // Avvia l'operazione di drag & drop con effetto "Sposta"
            listViewClips.DoDragDrop(items, DragDropEffects.Move);
        }

        // Verifica che il tipo di dato sia accettabile quando si entra nel TreeView
        private void treeViewFolders_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(List<ListViewItem>)))
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.None;
        }

        // Evidenzia il nodo target mentre si passa sopra
        private void treeViewFolders_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(List<ListViewItem>)))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;

            // Evidenzia il nodo sotto al mouse
            Point pt = treeViewFolders.PointToClient(new Point(e.X, e.Y));
            TreeNode nodeUnderMouse = treeViewFolders.GetNodeAt(pt);
            if (nodeUnderMouse != null && treeViewFolders.SelectedNode != nodeUnderMouse)
            {
                treeViewFolders.SelectedNode = nodeUnderMouse;
            }
        }

        // Esegue lo spostamento finale quando l’utente rilascia il mouse
        private void treeViewFolders_DragDrop(object sender, DragEventArgs e)
        {
            // 1. Validazione tipo dati
            if (!e.Data.GetDataPresent(typeof(List<ListViewItem>)))
                return;

            // 2. Nodo target sotto al cursore
            var items = (List<ListViewItem>)e.Data.GetData(typeof(List<ListViewItem>));
            Point pt = treeViewFolders.PointToClient(new Point(e.X, e.Y));
            TreeNode targetNode = treeViewFolders.GetNodeAt(pt);
            if (targetNode == null)
                return;

            int destFolderId;
            if (!int.TryParse(targetNode.Name, out destFolderId))
                return;

            // 3. Blocca destinazioni vietate
            int[] blockedFolderIds = { 1, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14 }; // cartelle vietate come destinazione

            if (blockedFolderIds.Contains(destFolderId))
            {
                MessageBox.Show("ATTENZIONE: Non puoi spostare clip in questa cartella.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 4. Esegui spostamento nel DB
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();

                foreach (ListViewItem item in items)
                {
                    string idClip = item.SubItems[8].Text; // ID della clip

                    // Recupera la cartella di origine della clip
                    int currentFolderId = -1;
                    using (var getCmd = new SQLiteCommand("SELECT folder_id FROM clips WHERE id = @id", conn))
                    {
                        getCmd.Parameters.AddWithValue("@id", idClip);
                        object result = getCmd.ExecuteScalar();
                        if (result != null)
                            currentFolderId = Convert.ToInt32(result);
                    }

                    // Blocca lo spostamento se proviene da cartella vietata
                    if (blockedFolderIds.Contains(currentFolderId))
                    {
                        MessageBox.Show("Non puoi spostare clip da questa cartella.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }

                    // Sposta la clip ed esegue l'aggiornamento nel DB
                    using (var updateCmd = new SQLiteCommand("UPDATE clips SET folder_id = @folderId WHERE id = @id", conn))
                    {
                        updateCmd.Parameters.AddWithValue("@folderId", destFolderId);
                        updateCmd.Parameters.AddWithValue("@id", idClip);
                        updateCmd.ExecuteNonQuery();
                    }
                }
            }

            // Ricarica lista clip
            RefreshListViewForSelectedNode();
        }

        private void LoadFavoriteClips()
        {
            // Svuota la ListView prima di caricare nuovi elementi
            listViewClips.Items.Clear();

            try
            {
                // Costruisce il percorso completo al file del database SQLite
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";

                string sql = @"SELECT id, title, type, created_at, source, image_key, is_favorite, is_locked, folder_id, size_bytes
                           FROM clips WHERE is_favorite = 1 ORDER BY created_at DESC";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        // Ciclo di lettura dei risultati del database
                        while (reader.Read())
                        {
                            // Estrae il titolo e lo limita alla prima riga e max 60 caratteri
                            string titolofull = reader["title"].ToString();
                            string primaRiga = titolofull.Split(new[] { '\r', '\n' })[0];
                            string titolo = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                            
                            // Altre informazioni della clip
                            string data = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                            string tipo = reader["type"].ToString();
                            string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                            string cartella = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                            string origine = reader["source"].ToString();
                            
                            // Simboli visuali per preferiti e bloccati
                            string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                            string isLocked = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                            string id = reader["id"].ToString();

                            // Crea un nuovo ListViewItem e lo aggiunge alla ListView
                            ListViewItem item = new ListViewItem(new string[]
                            {
                                titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                            });

                            item.ImageKey = reader["image_key"].ToString();
                            listViewClips.Items.Add(item);
                        }
                    }
                }

                // Applica le righe alternate se ci sono risultati
                if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadTodayClips()
        {
            listViewClips.Items.Clear();

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";
                string today = DateTime.Now.ToString("yyyy-MM-dd");

                string sql = @"SELECT id, title, type, created_at, source, image_key, is_favorite, is_locked, folder_id, size_bytes
                           FROM clips WHERE DATE(created_at) = @today ORDER BY created_at DESC";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@today", today);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string titolofull = reader["title"].ToString();
                                string primaRiga = titolofull.Split(new[] { '\r', '\n' })[0];
                                string titolo = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                                string data = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                                string tipo = reader["type"].ToString();
                                string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                                string cartella = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                                string origine = reader["source"].ToString();
                                string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                                string isLocked = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                                string id = reader["id"].ToString();

                                ListViewItem item = new ListViewItem(new string[]
                            {
                                titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                            });

                                item.ImageKey = reader["image_key"].ToString();
                                listViewClips.Items.Add(item);
                            }
                        }
                    }
                }

                if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadWeekClips()
        {
            listViewClips.Items.Clear();

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";

                // Calcola la data di inizio della settimana (lunedì)
                DateTime oggi = DateTime.Today;
                int giorniDaSottrarre = (int)oggi.DayOfWeek - 1;
                if (giorniDaSottrarre < 0) giorniDaSottrarre = 6; // Se è domenica
                DateTime lunedi = oggi.AddDays(-giorniDaSottrarre);
                string dataInizioSettimana = lunedi.ToString("yyyy-MM-dd");

                string sql = @"SELECT id, title, type, created_at, source, image_key, is_favorite, is_locked, folder_id, size_bytes
                           FROM clips WHERE DATE(created_at) >= @inizioSettimana ORDER BY created_at DESC";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@inizioSettimana", dataInizioSettimana);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string titolofull = reader["title"].ToString();
                                string primaRiga = titolofull.Split(new[] { '\r', '\n' })[0];
                                string titolo = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                                string data = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                                string tipo = reader["type"].ToString();
                                string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                                string cartella = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                                string origine = reader["source"].ToString();
                                string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                                string isLocked = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                                string id = reader["id"].ToString();

                                ListViewItem item = new ListViewItem(new[]
                            {
                                titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                            });

                                item.ImageKey = reader["image_key"].ToString();
                                listViewClips.Items.Add(item);
                            }
                        }
                    }
                }

                if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadMonthClips()
        {
            listViewClips.Items.Clear();

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";

                // Calcola il primo giorno del mese corrente
                DateTime primoGiornoDelMese = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                string dataInizioMese = primoGiornoDelMese.ToString("yyyy-MM-dd");

                string sql = @"SELECT id, title, type, created_at, source, image_key, is_favorite, is_locked, folder_id, size_bytes
                           FROM clips WHERE DATE(created_at) >= @inizioMese ORDER BY created_at DESC";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@inizioMese", dataInizioMese);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string titolofull = reader["title"].ToString();
                                string primaRiga = titolofull.Split(new[] { '\r', '\n' })[0];
                                string titolo = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                                string data = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                                string tipo = reader["type"].ToString();
                                string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                                string cartella = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                                string origine = reader["source"].ToString();
                                string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                                string isLocked = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                                string id = reader["id"].ToString();

                                ListViewItem item = new ListViewItem(new[]
                            {
                                titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                            });

                                item.ImageKey = reader["image_key"].ToString();
                                listViewClips.Items.Add(item);
                            }
                        }
                    }
                }

                if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadAllClips()
        {
            listViewClips.Items.Clear();

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";

                string sql = @"SELECT id, title, type, created_at, source, image_key, is_favorite, is_locked, folder_id, size_bytes
                           FROM clips ORDER BY created_at DESC";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string titolofull = reader["title"].ToString();
                            string primaRiga = titolofull.Split(new[] { '\r', '\n' })[0];
                            string titolo = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                            string data = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                            string tipo = reader["type"].ToString();
                            string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                            string cartella = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                            string origine = reader["source"].ToString();
                            string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                            string isLocked = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                            string id = reader["id"].ToString();

                            ListViewItem item = new ListViewItem(new[]
                        {
                            titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                        });

                            item.ImageKey = reader["image_key"].ToString();
                            listViewClips.Items.Add(item);
                        }
                    }
                }
                if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadClipsByType(string tipoClip)
        {
            listViewClips.Items.Clear();

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                string connStr = "Data Source=" + dbPath + ";Version=3;";

                string sql = @"SELECT id, title, type, created_at, source, image_key, is_favorite, is_locked, folder_id, size_bytes
                           FROM clips WHERE type = @tipo ORDER BY created_at DESC";

                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@tipo", tipoClip);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string titolofull = reader["title"].ToString();
                                string primaRiga = titolofull.Split(new[] { '\r', '\n' })[0];
                                string titolo = primaRiga.Length > 60 ? primaRiga.Substring(0, 57) + "..." : primaRiga;
                                string data = Convert.ToDateTime(reader["created_at"]).ToString("dd/MM/yyyy HH:mm:ss");
                                string tipo = reader["type"].ToString();
                                string dimensione = reader["size_bytes"] != DBNull.Value ? FormatSize(Convert.ToInt64(reader["size_bytes"])) : "";
                                string cartella = GetFolderNameById(Convert.ToInt32(reader["folder_id"]), conn);
                                string origine = reader["source"].ToString();
                                string isFavorite = Convert.ToInt32(reader["is_favorite"]) == 1 ? "★" : "";
                                string isLocked = Convert.ToInt32(reader["is_locked"]) == 1 ? "🔒" : "";
                                string id = reader["id"].ToString();

                                ListViewItem item = new ListViewItem(new[]
                            {
                                titolo, data, tipo, dimensione, cartella, origine, isFavorite, isLocked, id
                            });

                                item.ImageKey = reader["image_key"].ToString();
                                listViewClips.Items.Add(item);
                            }
                        }
                    }
                }

                if (listViewClips.Items.Count > 0) { AlternatingRowColors(); }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento delle clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void treeViewFolders_AfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            // Se il nuovo nome è vuoto o composto solo da spazi, annulla la modifica
            if (string.IsNullOrWhiteSpace(e.Label))
            {
                e.CancelEdit = true;
                return;
            }

            // Ottiene l'oggetto FolderItem associato al nodo modificato
            FolderItem folder = e.Node.Tag as FolderItem;
            if (folder == null)
                return;

            // Rimuove eventuali spazi iniziali o finali dal nuovo nome
            string newName = e.Label.Trim();

            // Annulla l'operazione se il nuovo nome è identico a quello attuale (ignora maiuscole/minuscole)
            if (string.Compare(newName, folder.Name, true) == 0)
                return;

            // Recupera tutti i nodi dei fratelli (nodi con lo stesso genitore oppure tutti i nodi di primo livello)
            TreeNodeCollection siblings;
            if (e.Node.Parent != null)
                siblings = e.Node.Parent.Nodes;
            else
                siblings = treeViewFolders.Nodes;

            // Controlla se esiste già un altro nodo con lo stesso nome tra i fratelli
            foreach (TreeNode sibling in siblings)
            {
                if (sibling != e.Node && string.Compare(sibling.Text, newName, true) == 0)
                {
                    // Se il nome è duplicato mostra un avviso
                    MessageBox.Show("Esiste già una cartella con questo nome.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.CancelEdit = true;
                    return;
                }
            }

            // Percorso del database SQLite
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                // Aggiorna il nome nel database
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    string sql = "UPDATE folders SET name = @name WHERE id = @id";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", newName);
                        cmd.Parameters.AddWithValue("@id", folder.Id);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Aggiorna anche il nome nell'oggetto in memoria
                folder.Name = newName;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante la rinomina della cartella: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // === 3b. Eventi ListView ===
        private void listViewClips_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Nasconde tutte le anteprime se non è selezionata alcuna clip
            if (listViewClips.SelectedItems.Count == 0)
            {
                HideAllPreviews();
                toolStripStatusCollection.Text = "";
                toolStripStatusFormat.Text = "";
                toolStripStatusMIME.Text = "";
                toolStripStatusPosPreview.Text = "";
                return;
            }

            // Recupera la clip selezionata
            var selected = listViewClips.SelectedItems[0];
            string tipo = selected.SubItems[2].Text.ToLower();
            string idClip = selected.SubItems[8].Text;

            // Aggiorna la barra di stato con il tipo
            toolStripStatusFormat.Text = "Formato: " + tipo;

            // Recupera il contenuto dal DB
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";
            object content = null;
            string folderName = "";

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                string sql = @"SELECT c.content, f.name as folder_name
                               FROM clips c JOIN folders f ON c.folder_id = f.id
                               WHERE c.id = @id";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idClip);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            content = reader["content"];
                            folderName = reader["folder_name"].ToString();
                        }
                    }
                }
            }

            // Aggiorna lo stato con il nome cartella
            toolStripStatusCollection.Text = "Cartella: " + folderName;

            if (content == null)
            {
                ShowPlaceholder("Contenuto non disponibile.");
                return;
            }

            // Visualizza l’anteprima in base al tipo
            switch (tipo)
            {
                case "testo":
                case "link":
                    toolStripStatusMIME.Text = "text/plain (UTF-8)";
                    ShowTextPreview(Encoding.UTF8.GetString((byte[])content), tipo == "link");
                    break;

                case "richtext":
                    toolStripStatusMIME.Text = "text/rtf (UTF-8)";
                    ShowRichTextPreview(Encoding.UTF8.GetString((byte[])content));
                    break;

                case "immagine":
                    try
                    {
                        using (MemoryStream ms = new MemoryStream((byte[])content))
                        {
                            Image img = Image.FromStream(ms);
                            ShowImagePreview(img);

                            string mime = GetMimeTypeFromImage(img);
                            toolStripStatusMIME.Text = mime;
                        }
                    }
                    catch
                    {
                        toolStripStatusMIME.Text = "Formato immagine sconosciuto";
                        ShowPlaceholder("Impossibile visualizzare l'immagine.");
                    }
                    break;

                default:
                    ShowPlaceholder("Questo tipo non è visualizzabile in anteprima.");
                    break;
            }
        }

        // === 3c. Metodi di supporto e utilità ===
        private void StartClipboardMonitor()
        {
            // Avvia il timer per controllare il contenuto degli appunti ogni secondo.
            // Se cambia il contenuto del testo nella clipboard, viene salvato.
            clipboardTimer = new Timer();
            clipboardTimer.Interval = 1000; // Ogni 1000 ms (1 secondo)
            clipboardTimer.Tick += ClipboardTimer_Tick;
            clipboardTimer.Start();
        }

        private void StopClipboardMonitor()
        {
            if (clipboardTimer != null)
            {
                clipboardTimer.Stop();
                clipboardTimer.Tick -= ClipboardTimer_Tick;
                clipboardTimer.Dispose();
                clipboardTimer = null;
            }
        }

        private void ClipboardTimer_Tick(object sender, EventArgs e)
        {
            if (!Clipboard.ContainsData(DataFormats.Text) &&
                !Clipboard.ContainsData(DataFormats.Rtf) &&
                !Clipboard.ContainsData(DataFormats.Bitmap) &&
                !Clipboard.ContainsData(DataFormats.FileDrop))
            {
                return; // Nessun contenuto rilevante
            }

            IDataObject data = Clipboard.GetDataObject();
            if (data == null)
                return;

            string titolo = null;
            string tipo = null;
            byte[] contenuto = null;
            string origine = "Clipboard";
            string imageKey = null;

            // 1. Testo semplice
            if (data.GetDataPresent(DataFormats.Text))
            {
                string text = data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    contenuto = Encoding.UTF8.GetBytes(text);
                    titolo = text.Length > 100 ? text.Substring(0, 100) + "..." : text;

                    if (IsValidUrl(text))
                    {
                        tipo = "link";
                        imageKey = "link.png";
                    }
                    else
                    {
                        tipo = "testo";
                        imageKey = "text.png";
                    }
                }
            }
            // 2. Testo formattato (RTF)
            else if (data.GetDataPresent(DataFormats.Rtf))
            {
                string rtf = data.GetData(DataFormats.Rtf) as string;
                if (!string.IsNullOrEmpty(rtf))
                {
                    contenuto = Encoding.UTF8.GetBytes(rtf);
                    titolo = rtf.Length > 100 ? rtf.Substring(0, 100) + "..." : rtf;
                    tipo = "richtext";
                    imageKey = "richtext.png";
                }
            }
            // 3. Immagine
            else if (data.GetDataPresent(DataFormats.Bitmap))
            {
                Image img = data.GetData(DataFormats.Bitmap) as Image;
                if (img != null)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        contenuto = ms.ToArray();
                    }

                    titolo = "Screenshot " + DateTime.Now.ToString("HH:mm:ss");
                    tipo = "immagine";
                    imageKey = "image.png";
                }
            }
            // 4. File(s)
            else if (data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string joinedPaths = string.Join("\n", files);
                    contenuto = Encoding.UTF8.GetBytes(joinedPaths);
                    titolo = "File(s) " + DateTime.Now.ToString("HH:mm:ss");
                    tipo = "testo";
                    imageKey = "text.png";
                }
            }
            // 5. Formati personalizzati (OLE / binari)
            else
            {
                string[] formats = data.GetFormats();
                foreach (string format in formats)
                {
                    object raw = data.GetData(format);
                    if (raw is MemoryStream)
                    {
                        MemoryStream ms = (MemoryStream)raw;
                        contenuto = ms.ToArray();
                        tipo = "binario";
                        imageKey = "block.png";
                        break;
                    }
                    else if (raw is byte[])
                    {
                        byte[] bytes = (byte[])raw;
                        contenuto = bytes;
                        tipo = "binario";
                        imageKey = "block.png";
                        break;
                    }
                }

                if (contenuto != null && tipo == "binario")
                    titolo = "Oggetto OLE";
            }

            // Contenuto vuoto o duplicato → non salvare
            if (contenuto == null ||
                (lastClipboardContent != null && contenuto.SequenceEqual(lastClipboardContent)))
                return;

            // Salva nel database
            int folderId = 2; // InBox
            SaveClipToDatabase(titolo, tipo, contenuto, origine, imageKey, folderId);

            // Aggiorna lista
            RefreshListViewForSelectedNode();

            // Memorizza l'ultimo contenuto clipboard
            lastClipboardContent = contenuto;
        }

        private void SaveClipToDatabase(string titolo, string tipo, byte[] contenuto, string origine, string imageKey, int folderId)
        {
            if (contenuto == null || contenuto.Length > MaxContentSizeBytes)
            {
                MessageBox.Show("ATTENZIONE: Il contenuto è troppo grande per essere salvato (max 5 MB).", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    string sql = @"INSERT INTO clips (title, type, content, size_bytes, created_at, source, image_key, folder_id)
                                   VALUES (@title, @type, @content, @size, @created_at, @source, @imageKey, @folderId);";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@title", titolo);
                        cmd.Parameters.AddWithValue("@type", tipo);
                        cmd.Parameters.AddWithValue("@content", contenuto);
                        cmd.Parameters.AddWithValue("@size", contenuto != null ? contenuto.Length : 0);
                        cmd.Parameters.AddWithValue("@source", origine);
                        cmd.Parameters.AddWithValue("@imageKey", imageKey);
                        cmd.Parameters.AddWithValue("@created_at", DateTime.Now);
                        cmd.Parameters.AddWithValue("@folderId", folderId);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il salvataggio della clip: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateCursorPositionInStatusBar(object sender, EventArgs e)
        {
            // Verifica se l'anteprima è visibile e se il controllo interno esiste
            if (!previewRichTextBox.Visible || previewRichTextBox.InnerRichTextBox == null)
            {
                toolStripStatusPosPreview.Text = "";
                return;
            }

            // Recuperiamo la posizione del cursore nell'editor
            var pos = previewRichTextBox.GetCursorPosition();

            // Verifichiamo che la posizione sia valida (che non sia null e che non sia negativa)
            if (pos != null && pos.Line >= 0 && pos.Column >= 0)
            {
                // Mostra la riga e la colonna nella status bar
                toolStripStatusPosPreview.Text = string.Format("Riga: {0}, Colonna: {1}", pos.Line, pos.Column);
            }
            else
            {
                // In caso di valori null svuota la barra di stato
                toolStripStatusPosPreview.Text = "";
            }
        }

        private void HideAllPreviews()
        {
            // Nasconde tutte le anteprime visive (RichTextBox, PictureBox, Label)
            previewRichTextBox.Visible = false;
            previewImageBox.Visible = false;
            labelPlaceholder.Visible = false;
        }

        private void ShowTextPreview(string text, bool isLink = false)
        {
            // Nasconde tutti gli altri tipi di anteprima
            HideAllPreviews();

            // Pulisce e prepara l'editor per visualizzare il testo
            previewRichTextBox.InnerRichTextBox.Clear();
            previewRichTextBox.InnerRichTextBox.SelectionStart = 0;
            previewRichTextBox.InnerRichTextBox.SelectionLength = 0;

            // Se è un link viene colorato e sottolineato altrimenti viene assegnato il font standard
            previewRichTextBox.InnerRichTextBox.SelectionFont = new Font(
                previewRichTextBox.InnerRichTextBox.Font,
                isLink ? FontStyle.Underline : FontStyle.Regular);

            previewRichTextBox.InnerRichTextBox.SelectionColor = isLink ? Color.Blue : Color.Black;

            // Viene assegnato il testo
            previewRichTextBox.InnerRichTextBox.Text = text;
            previewRichTextBox.Visible = true;
        }


        private void ShowRichTextPreview(string rtf)
        {
            // Mostra anteprima di testo formattato RTF
            HideAllPreviews();

            // Pulisce e carica il contenuto RTF
            previewRichTextBox.InnerRichTextBox.Clear();
            previewRichTextBox.InnerRichTextBox.Rtf = rtf;
            previewRichTextBox.Visible = true;
        }

        private void ShowImagePreview(Image img)
        {
            // Mostra anteprima immagine
            HideAllPreviews();

            // Assegna l'immagine al controllo personalizzato di anteprima
            previewImageBox.Image = img;

            //Forza l'applicazione del mode corrente
            previewImageBox.SetViewMode(previewImageBox.ViewMode);

            // Rende visibile il controllo dell'immagine
            previewImageBox.Visible = true;
        }

        private void ShowPlaceholder(string message)
        {
            // Mostra messaggio generico (per tipi di dati non supportati)
            HideAllPreviews();

            // Visualizza un messaggio descrittivo al posto del contenuto
            labelPlaceholder.Text = message;
            labelPlaceholder.Visible = true;
        }

        private string GetMimeTypeFromImage(Image img)
        {
            if (img.RawFormat.Equals(ImageFormat.Jpeg))
                return "image/jpeg";
            if (img.RawFormat.Equals(ImageFormat.Png))
                return "image/png";
            if (img.RawFormat.Equals(ImageFormat.Gif))
                return "image/gif";
            if (img.RawFormat.Equals(ImageFormat.Bmp))
                return "image/bmp";
            if (img.RawFormat.Equals(ImageFormat.Tiff))
                return "image/tiff";
            if (img.RawFormat.Equals(ImageFormat.Icon))
                return "image/x-icon";

            return "image/unknown";
        }

        private string GetCurrentSelectedNodeTag()
        {
            if (treeViewFolders.SelectedNode != null)
                return treeViewFolders.SelectedNode.Name;
            return string.Empty;
        }

        private void SearchBar_SearchRequested(object sender, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            bool caseSensitive = searchBar.CaseSensitive;

            PerformSearch(text, caseSensitive);
        }

        private void PerformSearch(string text, bool caseSensitive)
        {
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            listViewClips.BeginUpdate();
            listViewClips.Items.Clear();

            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    string sql = "SELECT c.id, c.type, c.content, c.created_at, f.name AS folder_name " +
                                  "FROM clips c " +
                                  "LEFT JOIN folders f ON c.folder_id = f.id " +
                                  "WHERE c.type IN ('testo', 'richtext', 'link')";

                    using (var cmd = new SQLiteCommand(sql, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string type = reader["type"].ToString();
                            byte[] raw = (byte[])reader["content"];
                            string content = Encoding.UTF8.GetString(raw);

                            bool match = caseSensitive
                                ? content.Contains(text)
                                : content.ToLower().Contains(text.ToLower());

                            if (match)
                            {
                                string id = reader["id"].ToString();
                                string created = reader["created_at"].ToString();
                                string folder = reader["folder_name"] != DBNull.Value ? reader["folder_name"].ToString() : "";

                                // Puoi personalizzare le colonne
                                var item = new ListViewItem(new string[] {
                                    content.Substring(0, Math.Min(100, content.Length)), // Titolo
                                    created,                                             // Data/Ora
                                    type,                                                // Tipo
                                    string.Format("{0} Byte", raw.Length),               // Dimensione
                                    folder,                                              // Cartella
                                    "RICERCA",                                           // Origine
                                    "",                                                  // Preferiti
                                    "",                                                  // Bloccato
                                    id                                                   // ID
                                    });

                                listViewClips.Items.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante la ricerca:\n\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                listViewClips.EndUpdate();
            }
        }

        // === 4. Eventi UI principali ===
        private void toolStripFileMenu_DropDownOpening(object sender, EventArgs e)
        {
            // Attiva o disattiva la voce "Stampa" in base alla selezione delle clip
            toolStripFileMenuPrint.Enabled = (listViewClips.SelectedItems.Count > 0);
        }

        private void toolStripFileMenuNewClip_Click(object sender, EventArgs e)
        {
            // Ottieni l'ID della cartella attualmente selezionata nel TreeView
            string folderIdStr = GetCurrentSelectedNodeTag();
            int folderId;

            // Verifica che l'ID sia un numero valido
            if (!int.TryParse(folderIdStr, out folderId))
            {
                MessageBox.Show("Nessuna cartella selezionata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Definisce i dati della nuova clip vuota
            string titolo = "Nuova clip vuota";
            string tipo = "testo";
            string origine = "Manuale";
            string imageKey = "text.png";
            byte[] contenuto = Encoding.UTF8.GetBytes(""); // clip vuota

            // Salva la nuova clip nel database
            SaveClipToDatabase(titolo, tipo, contenuto, origine, imageKey, folderId);

            // Ricarica la ListView per riflettere l’aggiunta
            RefreshListViewForSelectedNode();
        }

        private void toolStripFileMenuClipProperties_Click(object sender, EventArgs e)
        {
            // Verifica che sia selezionata una clip
            if (listViewClips.SelectedItems.Count == 0)
            {
                MessageBox.Show("Nessuna clip selezionata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Recupera l’elemento selezionato dalla ListView
            var item = listViewClips.SelectedItems[0];

            // Costruisce una stringa con le informazioni della clip
            string info = "Titolo: " + item.SubItems[0].Text + "\n" +
                          "Data/Ora: " + item.SubItems[1].Text + "\n" +
                          "Tipo: " + item.SubItems[2].Text + "\n" +
                          "Dimensione: " + item.SubItems[3].Text + "\n" +
                          "Cartella: " + item.SubItems[4].Text + "\n" +
                          "Origine: " + item.SubItems[5].Text + "\n" +
                          "Preferita: " + item.SubItems[6].Text + "\n" +
                          "Bloccata: " + item.SubItems[7].Text + "\n" +
                          "ID Clip: " + item.SubItems[8].Text;  // ID della clip

            // Mostra un messaggio informativo con le proprietà della clip
            MessageBox.Show(info, "Proprietà Clip", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void toolStripFileMenuNewCollection_Click(object sender, EventArgs e)
        {
            const int parentIdUtente = 3;                      // ID cartella Utente
            const string imageKey = "user.png";                // Icona cartelle utente
            const string nomeProvvisorio = "Nuova collezione"; // Nome temporaneo

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";
            int newFolderId = -1; // ID per la creazione della nuova cartella

            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // Prima unserisce la nuova cartella nel DB...
                    string sql = @"INSERT INTO folders (name, parent_id, image_key, is_locked, is_protected, sort_order)
                                   VALUES (@name, @parentId, @imageKey, 0, 0, 0);
                                   SELECT last_insert_rowid();"; // ... e poi restituisce l'ID della nuova riga

                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", nomeProvvisorio);
                        cmd.Parameters.AddWithValue("@parentId", parentIdUtente);
                        cmd.Parameters.AddWithValue("@imageKey", imageKey);

                        // Esegue e salva l'ID della nuova cartella
                        newFolderId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }

                InitializeTreeView(); // Ricarica l'albero delle cartelle

                // Trova nel TreeView il nodo corrispondente alla nuova cartella
                TreeNode newNode = FindNodeById(treeViewFolders.Nodes, newFolderId);
                if (newNode != null)
                {
                    // Seleziona e rende visibile il nuovo nodo
                    treeViewFolders.SelectedNode = newNode;
                    newNode.EnsureVisible();

                    // Abilita la modifica del nome (ovvero rename diretto nel TreeView)
                    treeViewFolders.LabelEdit = true;
                    newNode.BeginEdit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante la creazione della cartella: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private TreeNode FindNodeById(TreeNodeCollection nodes, int idToFind)
        {
            // Scorre tutti i nodi nella collezione corrente
            foreach (TreeNode node in nodes)
            {
                // Confronta il nome del nodo con l'ID da cercare (convertito in stringa)
                if (node.Name == idToFind.ToString())
                    return node; // OK, nodo trovato! ...e restituisce il riferimento

                // Altrimenti cerca ricorsivamente tra i nodi figli
                TreeNode found = FindNodeById(node.Nodes, idToFind);
                if (found != null)
                    return found; // Nodo trovato tra i discendenti
            }
            return null; // Nessun nodo trovato in questa collezione
        }

        private void toolStripFileMenuCollectionProperties_Click(object sender, EventArgs e)
        {
            if (treeViewFolders.SelectedNode == null)
            {
                MessageBox.Show("Nessuna cartella selezionata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            FolderItem folder = treeViewFolders.SelectedNode.Tag as FolderItem;
            if (folder == null)
            {
                MessageBox.Show("Errore nel recupero delle informazioni della cartella.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string info = "Nome: " + folder.Name + "\n" +
                          "ID: " + folder.Id + "\n" +
                          "Padre: " + (folder.ParentId.HasValue ? folder.ParentId.ToString() : "Nessuno") + "\n" +
                          "Bloccata: " + (folder.IsLocked ? "Sì" : "No");

            MessageBox.Show(info, "Proprietà Raccolta", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void toolStripFileMenuCollectionUpdate_Click(object sender, EventArgs e)
        {
            InitializeTreeView();
            InitializeListView();
            RefreshListViewForSelectedNode();
        }

        private void toolStripFileMenuCollectionEmptyTrash_Click(object sender, EventArgs e)
        {
            const int trashFolderId = 6; // ID fisso della cartella Cestino

            // Chiedi conferma all'utente
            DialogResult result = MessageBox.Show(
                "ATTENZIONE: Sei sicuro di voler eliminare DEFINITIVAMENTE tutte le clip presenti nel Cestino?\n\nQuesta operazione non può essere annullata.",
                "ClipM8++",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes)
                return;

            try
            {
                string connStr = "Data Source=clipm8_data.db;Version=3;";
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("DELETE FROM clips WHERE folder_id = @folderId", conn))
                    {
                        cmd.Parameters.AddWithValue("@folderId", trashFolderId);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Aggiorna la UI
                InitializeListView();
                RefreshListViewForSelectedNode();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante l'eliminazione: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripFileMenuDBToolsBackup_Click(object sender, EventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Title = "Salva backup del database";
            dlg.Filter = "Database SQLite (*.db)|*.db";
            dlg.InitialDirectory = Application.StartupPath;
            dlg.FileName = "clipm8_backup_" + DateTime.Now.ToString("ddMMyyyy_HHmm") + ".db";

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
                File.Copy(dbPath, dlg.FileName, true);

                MessageBox.Show("Backup salvato in:\n" + dlg.FileName, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il backup:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripFileMenuDBToolsRestore_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Seleziona file di backup da ripristinare";
            dlg.Filter = "Database SQLite (*.db)|*.db";
            dlg.InitialDirectory = Application.StartupPath;

            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");

                // Backup automatico del file attuale
                string safeCopy = dbPath + ".bak";
                if (File.Exists(dbPath))
                    File.Copy(dbPath, safeCopy, true);

                File.Copy(dlg.FileName, dbPath, true);

                MessageBox.Show("Database ripristinato con successo.\nBackup salvato come clipm8_data.db.bak", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);

                InitializeTreeView();
                InitializeListView();
                RefreshListViewForSelectedNode();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il ripristino:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripFileMenuDBToolsRepair_Click(object sender, EventArgs e)
        {
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // Verifica integrità del database
                    using (SQLiteCommand checkCmd = new SQLiteCommand("PRAGMA integrity_check;", conn))
                    {
                        string result = (string)checkCmd.ExecuteScalar();

                        if (result != "ok")
                        {
                            MessageBox.Show("Errore nel database: il database presenta problemi di integrità: " + result, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }

                    // Se tutto OK, procedi con la compattazione
                    using (SQLiteCommand vacuumCmd = new SQLiteCommand("VACUUM;", conn))
                    {
                        vacuumCmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("Manutenzione completata: il database è stato verificato e compattato con successo.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("ERRORE: Si è verificato un errore durante la manutenzione del database: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripFileMenuPrint_Click(object sender, EventArgs e)
        {
            if (listViewClips.SelectedItems.Count == 0)
            {
                MessageBox.Show("Seleziona una clip da stampare.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string idClip = listViewClips.SelectedItems[0].SubItems[8].Text;
            string tipo = listViewClips.SelectedItems[0].SubItems[2].Text.ToLower();

            // Controlla che il tipo sia supportato per la stampa
            if (tipo != "testo" && tipo != "link" && tipo != "richtext" && tipo != "immagine")
            {
                MessageBox.Show("Questo tipo di contenuto non può essere stampato.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";
            object content = null;

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    string sql = "SELECT content FROM clips WHERE id = @id";
                    using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", idClip);
                        content = cmd.ExecuteScalar();
                    }
                }

                if (content == null)
                {
                    MessageBox.Show("ATTENZIONE: contenuto non disponibile per la stampa.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                printContent = (byte[])content;
                printType = tipo;

                printDialog1.Document = printDocument1;

                if (printDialog1.ShowDialog() == DialogResult.OK)
                {
                    printDocument1.Print();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il caricamento della clip:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripFileMenuExit_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("Sei sicuro di voler uscire dal programma?", "Conferma uscita",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                this.Close();
            }
        }

        private void toolStripEditMenu_DropDownOpening(object sender, EventArgs e)
        {
            // Se non c'è alcuna clip selezionata, disattiva tutto
            if (listViewClips.SelectedItems.Count == 0)
            {
                toolStripEditMenuDeleteClip.Enabled = false;
                toolStripEditMenuFavorites.Enabled = false;
                toolStripEditMenuBlockClip.Enabled = false;
                return;
            }

            // Attiva le voci del menu
            toolStripEditMenuDeleteClip.Enabled = true;
            toolStripEditMenuFavorites.Enabled = true;
            toolStripEditMenuBlockClip.Enabled = true;

            // Usa il primo elemento selezionato come riferimento
            ListViewItem selected = listViewClips.SelectedItems[0];

            // Verifica se la clip è già nei preferiti (colonna 4)
            bool isFavorite = selected.SubItems[6].Text == "★";
            toolStripEditMenuFavorites.Text = isFavorite
                ? "Rimuovi clip dai preferiti"
                : "Aggiungi clip ai preferiti";

            // Verifica se la clip è bloccata (colonna 5)
            bool isLocked = selected.SubItems[7].Text == "🔒";
            toolStripEditMenuBlockClip.Text = isLocked
                ? "Sblocca clip selezionata"
                : "Blocca clip selezionata";
        }

        private void toolStripEditMenuDeleteClip_Click(object sender, EventArgs e)
        {
            // Verifica che ci siano clip selezionate nella ListView
            if (listViewClips.SelectedItems.Count == 0)
            {
                MessageBox.Show("Seleziona almeno una clip da eliminare.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Mostra una finestra di conferma all'utente
            if (MessageBox.Show("Vuoi spostare le clip selezionate nel Cestino?", "Conferma eliminazione",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            const int trashFolderId = 6; // ID fisso della cartella Cestino (cartella di sistema)

            // Percorso e stringa di connessione al database
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            // Contatori per il riepilogo finale
            int skipped = 0; // Clip bloccate non spostate
            int moved = 0;   // Clip effettivamente spostate

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        using (var checkCmd = conn.CreateCommand())
                        using (var moveCmd = conn.CreateCommand())
                        {
                            checkCmd.Transaction = transaction;
                            moveCmd.Transaction = transaction;

                            checkCmd.CommandText = "SELECT is_locked FROM clips WHERE id = @id";
                            checkCmd.Parameters.Add("@id", System.Data.DbType.Int32);

                            moveCmd.CommandText = "UPDATE clips SET folder_id = @folderId WHERE id = @id";
                            moveCmd.Parameters.Add("@folderId", System.Data.DbType.Int32);
                            moveCmd.Parameters.Add("@id", System.Data.DbType.Int32);

                            // Ciclo su ogni clip selezionata
                            foreach (ListViewItem item in listViewClips.SelectedItems)
                            {
                                int idClip = int.Parse(item.SubItems[8].Text); // ID della clip
                                string titoloClip = item.SubItems[0].Text;

                                checkCmd.Parameters["@id"].Value = idClip;
                                object result = checkCmd.ExecuteScalar();

                                if (result != null && Convert.ToInt32(result) == 1)
                                {
                                    skipped++;
                                    MessageBox.Show("ATTENZIONE: Impossibile cancellare la clip \"" + titoloClip + "\" perché è bloccata.",
                                                    "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    continue;
                                }

                                moveCmd.Parameters["@id"].Value = idClip;
                                moveCmd.Parameters["@folderId"].Value = trashFolderId;
                                moveCmd.ExecuteNonQuery();
                                moved++;
                            }
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show("Errore durante l'eliminazione delle clip:\n\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            // Aggiorna la lista delle clip per riflettere le modifiche
            RefreshListViewForSelectedNode();

        }

        private void toolStripEditMenuFavorites_Click(object sender, EventArgs e)
        {
            if (listViewClips.SelectedItems.Count == 0)
            {
                MessageBox.Show("Nessuna clip selezionata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();

                foreach (ListViewItem item in listViewClips.SelectedItems)
                {
                    int idClip = int.Parse(item.SubItems[8].Text); // ID della clip
                    int isFavorite = 0;

                    using (var readCmd = new SQLiteCommand("SELECT is_favorite FROM clips WHERE id = @id", conn))
                    {
                        readCmd.Parameters.AddWithValue("@id", idClip);
                        object result = readCmd.ExecuteScalar();
                        if (result != null)
                            isFavorite = Convert.ToInt32(result);
                    }

                    int newValue = isFavorite == 1 ? 0 : 1;

                    using (var cmd = new SQLiteCommand("UPDATE clips SET is_favorite = @fav WHERE id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@fav", newValue);
                        cmd.Parameters.AddWithValue("@id", idClip);
                        cmd.ExecuteNonQuery();
                    }

                    // Aggiorna la UI
                    item.SubItems[6].Text = (newValue == 1) ? "★" : "";
                }
            }

            RefreshListViewForSelectedNode();
        }

        private void toolStripEditMenuBlockClip_Click(object sender, EventArgs e)
        {
            if (listViewClips.SelectedItems.Count == 0)
            {
                MessageBox.Show("Nessuna clip selezionata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();

                foreach (ListViewItem item in listViewClips.SelectedItems)
                {
                    int idClip = int.Parse(item.SubItems[8].Text); // ID clip
                    int isLocked = 0;

                    // Legge lo stato attuale dal DB
                    using (var readCmd = new SQLiteCommand("SELECT is_locked FROM clips WHERE id = @id", conn))
                    {
                        readCmd.Parameters.AddWithValue("@id", idClip);
                        object result = readCmd.ExecuteScalar();
                        if (result != null)
                            isLocked = Convert.ToInt32(result);
                    }

                    // Calcola il nuovo stato da impostare
                    int newValue = (isLocked == 1) ? 0 : 1;

                    // Aggiorna lo stato nel DB
                    using (var updateCmd = new SQLiteCommand("UPDATE clips SET is_locked = @lock WHERE id = @id", conn))
                    {
                        updateCmd.Parameters.AddWithValue("@lock", newValue);
                        updateCmd.Parameters.AddWithValue("@id", idClip);
                        updateCmd.ExecuteNonQuery();
                    }

                    // Aggiorna la UI
                    item.SubItems[7].Text = (newValue == 1) ? "🔒" : "";
                }
            }
        }

        private void toolStripEditMenuDeleteFolder_Click(object sender, EventArgs e)
        {
            if (treeViewFolders.SelectedNode == null)
                return;

            TreeNode selectedNode = treeViewFolders.SelectedNode;

            // Estrai correttamente l'ID della cartella dal Tag
            FolderItem folderItem = selectedNode.Tag as FolderItem;
            int folderId = folderItem.Id;
            int parentId = folderItem.ParentId.Value;

            const int utenteFolderId = 3;

            // Solo sottocartelle dell'utente (parent_id = 3) possono essere eliminate
            if (parentId != utenteFolderId)
            {
                MessageBox.Show("ATTENZIONE: Puoi eliminare solo le cartelle personali create sotto la cartella 'Utente'.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Conferma
            string folderName = selectedNode.Text;
            DialogResult result = MessageBox.Show(
                string.Format("ATTENZIONE: Questa operazione eliminerà in modo PERMANENTE la cartella \"{0}\" e tutte le clip al suo interno.\n\nVuoi procedere?", folderName),
                              "ClipM8++", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            // Esegui eliminazione dal DB
            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        try
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.Transaction = transaction;

                                // Elimina tutte le clip nella cartella
                                cmd.CommandText = "DELETE FROM clips WHERE folder_id = @id";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@id", folderId);
                                cmd.ExecuteNonQuery();

                                // Elimina la cartella stessa
                                cmd.CommandText = "DELETE FROM folders WHERE id = @id";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@id", folderId);
                                cmd.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            transaction.Rollback();
                            MessageBox.Show("Errore durante l'eliminazione dalla cartella:\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }

                selectedNode.Remove();
                RefreshListViewForSelectedNode();

                MessageBox.Show("Cartella e clip eliminate correttamente.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante l'eliminazione:\n\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripEditMenuRenameFolder_Click(object sender, EventArgs e)
        {
            if (treeViewFolders.SelectedNode != null && treeViewFolders.LabelEdit)
            {
                treeViewFolders.SelectedNode.BeginEdit();
            }
            else
            {
                MessageBox.Show("Seleziona un nodo da rinominare.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void toolStripEditMenuBlockFolder_Click(object sender, EventArgs e)
        {
            TreeNode selectedNode = treeViewFolders.SelectedNode;

            if (selectedNode == null || selectedNode.Tag == null)
            {
                MessageBox.Show("Nessuna cartella selezionata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    // Ricava il nome del nodo selezionato
                    string folderName = selectedNode.Text;

                    // Controllo se la cartella è speciale
                    using (var cmd = new SQLiteCommand("SELECT is_locked, is_special FROM folders WHERE name = @name", conn))
                    {
                        cmd.Parameters.AddWithValue("@name", selectedNode.Text);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                int isSpecial = Convert.ToInt32(reader["is_special"]);
                                if (isSpecial == 1)
                                {
                                    MessageBox.Show("Questa cartella è speciale e non può essere bloccata.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    return;
                                }

                                int isLocked = Convert.ToInt32(reader["is_locked"]);
                                int newValue = isLocked == 1 ? 0 : 1;

                                using (var updateCmd = new SQLiteCommand("UPDATE folders SET is_locked = @lock WHERE name = @name", conn))
                                {
                                    updateCmd.Parameters.AddWithValue("@lock", newValue);
                                    updateCmd.Parameters.AddWithValue("@name", selectedNode.Text);
                                    updateCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore durante il blocco cartella:\n\n" + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripEditMenuFind_Click(object sender, EventArgs e)
        {
            // Inverti la visibilità
            searchBar.Visible = !searchBar.Visible;

            // Sincronizza la spunta del menu
            toolStripEditMenuFind.Checked = searchBar.Visible;

            // Se è visibile, dai focus alla casella di testo
            if (searchBar.Visible)
                searchBar.FocusTextBox();
        }

        private void toolStripEditMenuSelectAll_Click(object sender, EventArgs e)
        {
            // Se non ci sono elementi nella ListView mostra un messaggio ed esce
            if (listViewClips.Items.Count == 0)
            {
                MessageBox.Show("Non ci sono clip da selezionare.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Disabilita temporaneamente il ridisegno per migliorare le prestazioni durante l'aggiornamento
            listViewClips.BeginUpdate();

            // Seleziona tutti gli elementi presenti nella ListView
            foreach (ListViewItem item in listViewClips.Items)
            {
                item.Selected = true;
            }

            // Riabilita il ridisegno
            listViewClips.EndUpdate();

            // Imposta il focus sul controllo per garantire visibilità della selezione
            listViewClips.Focus();
        }

        private void toolStripEditMenuInvertSelection_Click(object sender, EventArgs e)
        {
            // Se non ci sono elementi nella ListView mostra un messaggio ed esce
            if (listViewClips.Items.Count == 0)
            {
                MessageBox.Show("Non ci sono clip da selezionare.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Disabilita il ridisegno della ListView per evitare flickering
            listViewClips.BeginUpdate();

            // Inverte la selezione di ogni elemento
            foreach (ListViewItem item in listViewClips.Items)
            {
                item.Selected = !item.Selected;
            }

            // Riabilita il ridisegno dopo l'operazione
            listViewClips.EndUpdate();

            // Imposta il focus per rendere visibile il risultato dell'inversione
            listViewClips.Focus();
        }

        private void toolStripViewMenuClipBrowserGrid_Click(object sender, EventArgs e)
        {
            // Inverti lo stato delle linee di griglia
            bool gridEnabled = !listViewClips.GridLines;
            listViewClips.GridLines = gridEnabled;

            // Aggiorna lo stato del menu (spuntato o meno)
            toolStripViewMenuClipBrowserGrid.Checked = gridEnabled;

            // Aggiorna il valore nel database (non distruttivo)
            SettingsDB.Set("listview_grid", gridEnabled.ToString().ToLower());
        }

        private void toolStripViewMenuClipBrowserAltRow_Click(object sender, EventArgs e)
        {
            // Inverti lo stato del menu
            toolStripViewMenuClipBrowserAltRow.Checked = !toolStripViewMenuClipBrowserAltRow.Checked;

            // Salva il nuovo stato nel DB
            SettingsDB.Set("listview_rows", toolStripViewMenuClipBrowserAltRow.Checked.ToString().ToLower());

            // Applica o rimuove l'effetto zebra alla ListView
            for (int i = 0; i < listViewClips.Items.Count; i++)
            {
                ListViewItem item = listViewClips.Items[i];
                item.BackColor = (toolStripViewMenuClipBrowserAltRow.Checked && i % 2 != 0) ? Color.LightGray : Color.White;
            }
        }

        private void toolStripViewMenuClipBrowserLargeIcons_Click(object sender, EventArgs e)
        {
            // Imposta la vista su "Icone grandi"
            listViewClips.View = View.LargeIcon;
            listViewClips.OwnerDraw = false;
            listViewClips.Invalidate(); // Forza ridisegno

            // Salva la modalità nel DB delle impostazioni
            SettingsDB.Set("listview_viewmode", "largeicon");

            // Deseleziona tutte le voci di modalità e spunta solo questa
            UncheckAllListViewViewModes();
            toolStripViewMenuClipBrowserLargeIcons.Checked = true;
        }

        private void toolStripViewMenuClipBrowserList_Click(object sender, EventArgs e)
        {
            listViewClips.View = View.List;
            listViewClips.OwnerDraw = false;
            listViewClips.Invalidate();

            SettingsDB.Set("listview_viewmode", "list");

            UncheckAllListViewViewModes();
            toolStripViewMenuClipBrowserList.Checked = true;
        }

        private void toolStripViewMenuClipBrowserDetails_Click(object sender, EventArgs e)
        {
            listViewClips.View = View.Details;
            listViewClips.OwnerDraw = false;
            listViewClips.Invalidate();

            SettingsDB.Set("listview_viewmode", "details");

            UncheckAllListViewViewModes();
            toolStripViewMenuClipBrowserDetails.Checked = true;
        }

        private void toolStripViewMenuClipBrowserTiles_Click(object sender, EventArgs e)
        {
            listViewClips.View = View.Tile;
            listViewClips.OwnerDraw = false;
            listViewClips.Invalidate();

            SettingsDB.Set("listview_viewmode", "tile");

            UncheckAllListViewViewModes();
            toolStripViewMenuClipBrowserTiles.Checked = true;
        }

        private void toolStripViewMenuPreviewWordwrap_Click(object sender, EventArgs e)
        {
            // Inverti lo stato corrente
            bool enabled = !toolStripViewMenuPreviewWordwrap.Checked;

            // Applica al controllo
            previewRichTextBox.WordWrap = enabled;

            // Aggiorna menu
            toolStripViewMenuPreviewWordwrap.Checked = enabled;

            // Salva nel database
            SettingsDB.Set("editor_wordwrap", enabled.ToString().ToLower());
        }

        private void toolStripViewMenuPreviewLineNumbers_Click(object sender, EventArgs e)
        {
            // Inverti lo stato attuale
            bool enabled = !toolStripViewMenuPreviewLineNumbers.Checked;

            // Applica al controllo
            previewRichTextBox.ShowLineNumbers = enabled;

            // Aggiorna il menu
            toolStripViewMenuPreviewLineNumbers.Checked = enabled;

            // Salva il valore nel database
            SettingsDB.Set("editor_linenumbers", enabled.ToString().ToLower());
        }

        private void toolStripViewMenuPreviewPanel_Click(object sender, EventArgs e)
        {
            bool isCollapsed = splitContainer1.Panel2Collapsed;

            splitContainer1.Panel2Collapsed = !isCollapsed;

            toolStripViewMenuPreviewPanel.Checked = isCollapsed;

            SettingsDB.Set("view_boxpreview", (isCollapsed).ToString().ToLower());
        }

        private void toolStripViewMenuLayoutNormal_Click(object sender, EventArgs e)
        {
            int width = 900;
            int height = 500;
            
            // Centra la finestra rispetto allo schermo principale
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;
            
            int posX = (screenWidth - width) / 2;
            int posY = (screenHeight - height) / 2;
            
            this.Size = new Size(width, height);
            this.Location = new Point(posX, posY);
        }

        private void toolStripViewMenuLayoutBottom_Click(object sender, EventArgs e)
        {
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;

            int height = 600; // Altezza fissa per layout basso
            int posX = 0;
            int posY = screenHeight - height;

            this.Size = new Size(screenWidth, height);
            this.Location = new Point(posX, posY);
        }

        private void toolStripViewMenuPreviewScaleImage_Click(object sender, EventArgs e)
        {
            previewImageBox.ViewMode = PreviewImageBox.ImageViewMode.Scale;

            UncheckAllImageViewModes();
            toolStripViewMenuPreviewScaleImage.Checked = true;

            SettingsDB.Set("image_view_mode", "scale");
        }

        private void toolStripViewMenuPreviewStretchImage_Click(object sender, EventArgs e)
        {
            previewImageBox.ViewMode = PreviewImageBox.ImageViewMode.Stretch;

            UncheckAllImageViewModes();
            toolStripViewMenuPreviewStretchImage.Checked = true;

            SettingsDB.Set("image_view_mode", "stretch");
        }

        private void toolStripViewMenuPreviewOriginalSize_Click(object sender, EventArgs e)
        {
            previewImageBox.ViewMode = PreviewImageBox.ImageViewMode.Normal;
        
            UncheckAllImageViewModes();
            toolStripViewMenuPreviewOriginalSize.Checked = true;
        
            SettingsDB.Set("image_view_mode", "normal");
        }

        private void toolStripViewMenuPreviewZoomIn_Click(object sender, EventArgs e)
        {
            previewImageBox.ZoomIn();
        }

        private void toolStripViewMenuPreviewZoomOut_Click(object sender, EventArgs e)
        {
            previewImageBox.ZoomOut();
        }

        private void toolStripViewMenuPreviewZoomReset_Click(object sender, EventArgs e)
        {
            previewImageBox.ResetZoom();
        }

        private void toolStripViewMenuToolbar_Click(object sender, EventArgs e)
        {
            // Inverti visibilità
            toolStrip1.Visible = !toolStrip1.Visible;

            // Allinea il check del menu
            toolStripViewMenuToolbar.Checked = toolStrip1.Visible;

            // Salva il nuovo stato nel DB
            SettingsDB.Set("view_toolbar", toolStrip1.Visible.ToString().ToLower());
        }

        private void toolStripViewMenuStatusbar_Click(object sender, EventArgs e)
        {
            // Inverti visibilità
            statusStrip1.Visible = !statusStrip1.Visible;

            // Allinea il check del menu
            toolStripViewMenuStatusbar.Checked = statusStrip1.Visible;

            // Salva il nuovo stato nel DB
            SettingsDB.Set("view_statusbar", statusStrip1.Visible.ToString().ToLower());
        }

        private void toolStripToolsMenuCaptureClips_Click(object sender, EventArgs e)
        {
            bool isCurrentlyEnabled = toolStripToolsMenuCaptureClips.Checked;

            if (isCurrentlyEnabled)
            {
                // Disattiva monitoraggio
                StopClipboardMonitor();
                toolStripToolsMenuCaptureClips.Checked = false;
                SettingsDB.Set("clipboard_monitor", "false");
            }
            else
            {
                // Attiva monitoraggio
                StartClipboardMonitor();
                toolStripToolsMenuCaptureClips.Checked = true;
                SettingsDB.Set("clipboard_monitor", "true");
            }
        }

        private void toolStripToolsMenuTransferClips_Click(object sender, EventArgs e)
        {
            // Verifica che una sola clip sia selezionata
            if (listViewClips.SelectedItems.Count != 1)
            {
                MessageBox.Show("ATTENZIONE: Seleziona una sola clip da trasferire.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var item = listViewClips.SelectedItems[0];

            if (item.SubItems.Count < 9)
            {
                MessageBox.Show("Colonne insufficienti. Impossibile leggere l'ID clip.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int idClip;
            if (!int.TryParse(item.SubItems[8].Text, out idClip))
            {
                MessageBox.Show("ID non valido nella colonna 8.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
            string connStr = "Data Source=" + dbPath + ";Version=3;";

            try
            {
                using (var conn = new SQLiteConnection(connStr))
                {
                    conn.Open();

                    string sql = "SELECT type, content FROM clips WHERE id = @id";
                    using (var cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", idClip);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string type = reader["type"].ToString();
                                byte[] raw = (byte[])reader["content"];

                                try
                                {
                                    // Trasferisce la clip negli appunti
                                    if (type == "testo" || type == "link")
                                    {
                                        string text = Encoding.UTF8.GetString(raw);
                                        Clipboard.SetText(text);
                                    }
                                    else if (type == "richtext")
                                    {
                                        string rtf = Encoding.UTF8.GetString(raw);
                                        Clipboard.SetData(DataFormats.Rtf, rtf);
                                    }
                                    else if (type == "immagine")
                                    {
                                        using (var ms = new MemoryStream(raw))
                                        {
                                            Image img = Image.FromStream(ms);
                                            Clipboard.SetImage(img);
                                        }
                                    }
                                    else
                                    {
                                        MessageBox.Show("Tipo di clip non supportato: " + type, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                        return;
                                    }

                                    // Incrementa il contatore usage_count
                                    using (var updateCmd = new SQLiteCommand("UPDATE clips SET usage_count = usage_count + 1 WHERE id = @id", conn))
                                    {
                                        updateCmd.Parameters.AddWithValue("@id", idClip);
                                        updateCmd.ExecuteNonQuery();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Errore durante il trasferimento: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                            else
                            {
                                MessageBox.Show("ATTENZIONE: Clip non trovata nel database.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore DB: " + ex.Message, "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void toolStripToolsMenuOptions_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Questa funzione è ancora in fase di progettazione.", "ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void toolStripHelpMenuAbout_Click(object sender, EventArgs e)
        {
            string message = "ClipM8++\n\n" +
                             "Versione 1.0\n" +
                             "© 2025 Francesco\n\n" +
                             "Applicazione per la gestione avanzata degli appunti.\n" +
                             "Salva, organizza e ritrova facilmente i tuoi contenuti copiati.\n\n" +
                             "Sviluppata in C# .NET Framework 4.0.";

            MessageBox.Show(message, "Informazioni su ClipM8++", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Chiedi conferma prima di chiudere
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Evita la conferma se la chiusura non è dovuta all'utente (es. spegnimento PC)
            if (e.CloseReason == CloseReason.UserClosing)
            {
                var result = MessageBox.Show(
                    "Sei sicuro di voler uscire?",
                    "Conferma uscita",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.No)
                {
                    e.Cancel = true; // Annulla la chiusura
                }
            }
        }

        // Salva impostazioni e rilascia risorse dopo la chiusura
        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Salva dimensione, posizione e stato della finestra
            SettingsDB.Set("window_state", this.WindowState.ToString());

            if (this.WindowState == FormWindowState.Normal)
            {
                SettingsDB.Set("window_left", this.Left.ToString());
                SettingsDB.Set("window_top", this.Top.ToString());
                SettingsDB.Set("window_width", this.Width.ToString());
                SettingsDB.Set("window_height", this.Height.ToString());
            }
            else
            {
                // Se la finestra è massimizzata, salviamo le dimensioni "RestoreBounds"
                SettingsDB.Set("window_left", this.RestoreBounds.Left.ToString());
                SettingsDB.Set("window_top", this.RestoreBounds.Top.ToString());
                SettingsDB.Set("window_width", this.RestoreBounds.Width.ToString());
                SettingsDB.Set("window_height", this.RestoreBounds.Height.ToString());
            }

            // Ferma eventuali thread o timer
            StopClipboardMonitor();

            // Log di uscita (facoltativo)
            Console.WriteLine("Applicazione chiusa correttamente.");
        }

        // === Classi interne ===
        private class FolderItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int? ParentId { get; set; }
            public string ImageKey { get; set; }
            public bool IsLocked { get; set; }
        }

        private class ClipItem
        {
            public string Titolo { get; set; }
            public string Tipo { get; set; }
            public DateTime DataOra { get; set; }
            public string Origine { get; set; }
            public string ID { get; set; }
            public string Categoria { get; set; }
        }

        private bool IsValidUrl(string url)
        {
            Uri uriResult;
            return Uri.TryCreate(url, UriKind.Absolute, out uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private string GetFolderNameById(int folderId, SQLiteConnection conn)
        {
            using (var cmd = new SQLiteCommand("SELECT name FROM folders WHERE id = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", folderId);
                object result = cmd.ExecuteScalar();
                return result != null ? result.ToString() : "";
            }
        }

        private string FormatSize(long sizeInBytes)
        {
            string[] sizeUnits = { "Byte", "KB", "MB", "GB", "TB" };
            double size = sizeInBytes;
            int unit = 0;

            while (size >= 1024 && unit < sizeUnits.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return size.ToString("0.0") + " " + sizeUnits[unit];
        }

        private void AlternatingRowColors()
        {
            if (!toolStripViewMenuClipBrowserAltRow.Checked)
                return;

            for (int i = 0; i < listViewClips.Items.Count; i++)
            {
                listViewClips.Items[i].BackColor = (i % 2 == 0) ? Color.White : Color.LightGray;
            }
        }

        private void UncheckAllListViewViewModes()
        {
            toolStripViewMenuClipBrowserLargeIcons.Checked = false;
            toolStripViewMenuClipBrowserList.Checked = false;
            toolStripViewMenuClipBrowserDetails.Checked = false;
            toolStripViewMenuClipBrowserTiles.Checked = false;
        }

        private void UncheckAllImageViewModes()
        {
            toolStripViewMenuPreviewScaleImage.Checked = false;
            toolStripViewMenuPreviewStretchImage.Checked = false;
            toolStripViewMenuPreviewOriginalSize.Checked = false;
        }
    }
}
