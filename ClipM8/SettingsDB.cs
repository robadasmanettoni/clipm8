using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using ClipM8.Properties;


public static class SettingsDB
{
    private static readonly string dbPath = Path.Combine(Application.StartupPath, "clipm8_data.db");
    private static readonly string connStr = "Data Source=" + dbPath + ";Version=3;";

    public static void InitializeDatabaseIfMissing()
    {
        // Se il file non esiste lo creiamo da zero
        if (!File.Exists(dbPath))
        {
            // Creazione file .db
            SQLiteConnection.CreateFile(dbPath);

            // Stringa di connessione per accedere al DB

            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();

                using (var cmd = new SQLiteCommand(conn))
                {
                    // === CREAZIONE DELLE TABELLE PRINCIPALI ===
                    // --- Creazione tabella FOLDERS ---
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS folders (
                                        id INTEGER PRIMARY KEY,
                                        name TEXT NOT NULL,
                                        parent_id INTEGER,
                                        image_key TEXT,
                                        is_locked INTEGER DEFAULT 0,
                                        is_special INTEGER DEFAULT 0,
                                        is_protected INTEGER DEFAULT 0,
                                        sort_order INTEGER DEFAULT 0);";
                    cmd.ExecuteNonQuery();

                    // --- Creazione tabella CLIPS ---
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS clips (
                                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        folder_id INTEGER NOT NULL,
                                        title TEXT NOT NULL,
                                        type TEXT NOT NULL,
                                        content BLOB,
                                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                                        size_bytes INTEGER,
                                        source TEXT,
                                        image_key TEXT NOT NULL,
                                        is_favorite INTEGER DEFAULT 0,
                                        is_locked INTEGER DEFAULT 0,
                                        color TEXT,
                                        usage_count INTEGER DEFAULT 0,
                                        note TEXT,
                                        FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE);";
                    cmd.ExecuteNonQuery();

                    // --- Creazione tabella TAGS_LIST ---
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS tags_list (
                                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        name TEXT NOT NULL,
                                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP);";
                    cmd.ExecuteNonQuery();

                    // --- Creazione tabella TAGS_CLIPS ---
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS tags_clips (
                                        clip_id INTEGER NOT NULL,
                                        tag_id INTEGER NOT NULL,
                                        PRIMARY KEY (clip_id, tag_id),
                                        FOREIGN KEY (clip_id) REFERENCES clips(id) ON DELETE CASCADE,
                                        FOREIGN KEY (tag_id) REFERENCES tags_list(id) ON DELETE CASCADE);";
                    cmd.ExecuteNonQuery();

                    // --- Creazione tabella SETTINGS ---
                    cmd.CommandText = @"CREATE TABLE IF NOT EXISTS settings (
                                        key TEXT PRIMARY KEY,
                                        value TEXT);";
                    cmd.ExecuteNonQuery();

                    // === INSERIMENTO VALORI PREDEFINITI ===
                    // --- Inserimento COLLEZIONI ---
                    cmd.CommandText = @"INSERT INTO folders (id, name, parent_id, image_key, is_locked, is_special, is_protected, sort_order) VALUES
                                        (1,  'Home',                  NULL, 'home.png',          0, 1, 0, 0),
                                        (2,  'InBox',                 1,    'inbox.png',         0, 1, 0, 1),
                                        (3,  'Utente',                1,    'user.png',          0, 1, 0, 2),
                                        (4,  'Preferiti',             1,    'favourites.png',    0, 1, 0, 3),
                                        (5,  'Ricerca',               1,    'docs-search.png',   0, 1, 0, 4),
                                        (6,  'Cestino',               1,    'recyclebin.png',    0, 1, 0, 5),
                                        (7,  'Oggi',                  5,    'date-today.png',    0, 1, 0, 0),
                                        (8,  'Questa settimana',      5,    'date-week.png',     0, 1, 0, 1),
                                        (9,  'Questo mese',           5,    'date-month.png',    0, 1, 0, 2),
                                        (10, 'Tutte le clip',         5,    'docs.png',          0, 1, 0, 3),
                                        (11, 'Tutti i testi normali', 5,    'docs-text.png',     0, 1, 0, 4),
                                        (12, 'Tutti i testi RTF',     5,    'docs-richtext.png', 0, 1, 0, 5),
                                        (13, 'Tutte le immagini',     5,    'docs-image.png',    0, 1, 0, 6),
                                        (14, 'Tutti i link',          5,    'docs-link.png',     0, 1, 0, 7);";
                    cmd.ExecuteNonQuery();

                    // --- Inserimento clip DEMO esempio (nella cartella HOME) ---
                    string insertDemoClips = @"INSERT INTO clips (folder_id, title, type, content, created_at, size_bytes, source, image_key, is_favorite, is_locked) VALUES 
                                               (1, 'Questo è un esempio di testo semplice copiato.', 'testo', @textSimple, @now1, @size1, 'Blocco note', 'text.png', 0, 0),
                                               (1, 'Questo è RTF!', 'richtext', @rtfText, @now2, @size2, 'WordPad', 'richtext.png', 0, 0),
                                               (1, 'Screenshot', 'immagine', @imageData, @now3, @size3, 'Snipping Tool', 'image.png', 0, 0),
                                               (1, 'https://openai.com', 'link', @linkData, @now4, @size4, 'Chrome', 'link.png', 0, 0),
                                               (1, 'Oggetto OLE', 'binario', @binaryData, @now5, @size5, 'ALTRO', 'block.png', 0, 0);";

                    using (var demoCmd = new SQLiteCommand(insertDemoClips, conn))
                    {
                        byte[] textSimple = Encoding.UTF8.GetBytes("Questo è un esempio di testo semplice copiato.");
                        byte[] rtfText = Encoding.UTF8.GetBytes(@"{\rtf1\ansi Questo è \b RTF\b0 !}");
                        byte[] linkData = Encoding.UTF8.GetBytes("https://openai.com");
                        byte[] binaryData = Encoding.UTF8.GetBytes("RAW_DATA_PLACEHOLDER");

                        byte[] imageData;
                        using (MemoryStream imgStream = new MemoryStream())
                        {
                            Resources.exampleImage.Save(imgStream, ImageFormat.Png);
                            imageData = imgStream.ToArray();
                        }

                        DateTime now = DateTime.Now;

                        demoCmd.Parameters.AddWithValue("@textSimple", textSimple);
                        demoCmd.Parameters.AddWithValue("@rtfText", rtfText);
                        demoCmd.Parameters.AddWithValue("@imageData", imageData);
                        demoCmd.Parameters.AddWithValue("@linkData", linkData);
                        demoCmd.Parameters.AddWithValue("@binaryData", binaryData);

                        demoCmd.Parameters.AddWithValue("@size1", textSimple.Length);
                        demoCmd.Parameters.AddWithValue("@size2", rtfText.Length);
                        demoCmd.Parameters.AddWithValue("@size3", imageData.Length);
                        demoCmd.Parameters.AddWithValue("@size4", linkData.Length);
                        demoCmd.Parameters.AddWithValue("@size5", binaryData.Length);

                        demoCmd.Parameters.AddWithValue("@now1", now);
                        demoCmd.Parameters.AddWithValue("@now2", now.AddMinutes(-5));
                        demoCmd.Parameters.AddWithValue("@now3", now.AddHours(-1));
                        demoCmd.Parameters.AddWithValue("@now4", now.AddMinutes(-10));
                        demoCmd.Parameters.AddWithValue("@now5", now.AddDays(-1));

                        // Esegue l'inserimento
                        demoCmd.ExecuteNonQuery();
                    }

                    // === Inserimento impostazioni predefinite ===
                    Rectangle screen = Screen.PrimaryScreen.WorkingArea;

                    int windowWidth = screen.Width;
                    int windowTop = (screen.Top + screen.Height / 2) - 100;
                    int windowHeight = (screen.Height / 2) + 100;
                    int windowLeft = 0;

                    string insertSettings = @"INSERT INTO settings (key, value) VALUES
                                              ('window_left',           @left),
                                              ('window_top',            @top),
                                              ('window_width',          @width),
                                              ('window_height',         @height),
                                              ('listview_grid',         'true'),
                                              ('listview_rows',         'false'),
                                              ('listview_viewmode',     'details'),
                                              ('editor_wordwrap',       'false'),
                                              ('editor_linenumbers',    'true'),
                                              ('image_view_mode',       'normal'),
                                              ('image_zoom_img',        '100'),
                                              ('image_zoom_txt',        '100'),
                                              ('view_boxpreview',       'true'),
                                              ('view_toolbar',          'true'),
                                              ('view_statusbar',        'true'),
                                              ('clipboard_monitor',     'true');";

                    using (var settingsCmd = new SQLiteCommand(insertSettings, conn))
                    {
                        settingsCmd.Parameters.AddWithValue("@left", windowLeft);
                        settingsCmd.Parameters.AddWithValue("@top", windowTop);
                        settingsCmd.Parameters.AddWithValue("@width", windowWidth);
                        settingsCmd.Parameters.AddWithValue("@height", windowHeight);
                        settingsCmd.ExecuteNonQuery();
                    }
                }

                // Chiusura connessione al DB
                conn.Close();
            }
        }
    }

    public static void Save(string key, string value)
    {
        using (var conn = new SQLiteConnection(connStr))
        {
            conn.Open();
            string sql = "REPLACE INTO settings (key, value) VALUES (@key, @value)";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public static string Load(string key, string defaultValue = "")
    {
        using (var conn = new SQLiteConnection(connStr))
        {
            conn.Open();
            string sql = "SELECT value FROM settings WHERE key = @key";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@key", key);
                object result = cmd.ExecuteScalar();
                return result != null ? result.ToString() : defaultValue;
            }
        }
    }

    public static bool Exists(string key)
    {
        using (var conn = new SQLiteConnection(connStr))
        {
            conn.Open();
            string sql = "SELECT 1 FROM settings WHERE key = @key LIMIT 1";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@key", key);
                var result = cmd.ExecuteScalar();
                return result != null;
            }
        }
    }

    public static void Update(string key, string value)
    {
        using (var conn = new SQLiteConnection(connStr))
        {
            conn.Open();
            string sql = "UPDATE settings SET value = @value WHERE key = @key";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public static void Set(string key, string value)
    {
        if (Exists(key))
            Update(key, value);
        else
            Save(key, value);
    }

    public static void Delete(string key)
    {
        using (var conn = new SQLiteConnection(connStr))
        {
            conn.Open();
            string sql = "DELETE FROM settings WHERE key = @key";
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@key", key);
                cmd.ExecuteNonQuery();
            }
        }
    }

    public static Dictionary<string, string> GetAll()
    {
        var settings = new Dictionary<string, string>();

        using (var conn = new SQLiteConnection(connStr))
        {
            conn.Open();
            string sql = "SELECT key, value FROM settings";
            using (var cmd = new SQLiteCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    settings[reader["key"].ToString()] = reader["value"].ToString();
                }
            }
        }

        return settings;
    }
}
