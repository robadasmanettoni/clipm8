using System;
using System.Collections;
using System.Windows.Forms;

/// <summary>
/// Comparatore personalizzato per ordinare i contenuti di una ListView.
/// Permette l'ordinamento cliccando sull'intestazione delle colonne.
/// </summary>
public class ListViewColumnSorter : IComparer
{
    // Indice della colonna su cui eseguire il confronto
    private int columnToSort;

    // Ordine di ordinamento attuale (crescente o decrescente)
    private SortOrder orderOfSort;

    /// <summary>
    /// Costruttore: inizializza l'ordinamento su nessuna colonna.
    /// </summary>
    public ListViewColumnSorter()
    {
        columnToSort = 0;
        orderOfSort = SortOrder.None;
    }

    /// <summary>
    /// Metodo principale per confrontare due elementi della ListView.
    /// </summary>
    public int Compare(object x, object y)
    {
        ListViewItem itemX = x as ListViewItem;
        ListViewItem itemY = y as ListViewItem;

        // Recupera il testo della colonna specificata da entrambe le righe
        string strX = itemX.SubItems[columnToSort].Text;
        string strY = itemY.SubItems[columnToSort].Text;

        // Confronto alfanumerico
        int result = string.Compare(strX, strY);

        // Inverti il risultato se l'ordine è decrescente
        if (orderOfSort == SortOrder.Descending)
            return -result;
        else if (orderOfSort == SortOrder.Ascending)
            return result;
        else
            return 0; // Nessun ordinamento
    }

    /// <summary>
    /// Colonna da ordinare (0-based)
    /// </summary>
    public int SortColumn
    {
        get { return columnToSort; }
        set { columnToSort = value; }
    }

    /// <summary>
    /// Ordine corrente (Ascendente, Discendente o Nessuno)
    /// </summary>
    public SortOrder Order
    {
        get { return orderOfSort; }
        set { orderOfSort = value; }
    }
}
