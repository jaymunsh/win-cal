using System.Windows;

namespace WinCal;

public partial class StickerEditWindow : Window
{
    private readonly Sticker _sticker;
    private readonly StickerStore _store;

    /// <summary>Result: true = saved, false = cancelled. Check Deleted for deletion.</summary>
    public bool Deleted { get; private set; }

    internal StickerEditWindow(Sticker sticker, StickerStore store)
    {
        _sticker = sticker;
        _store = store;
        InitializeComponent();
        Title = Loc.T("sticker_title");
        TitleText.Text = Loc.T("sticker_memo");
        SaveButton.Content = Loc.T("save");
        CancelButton.Content = Loc.T("cancel");
        DeleteButton.Content = Loc.T("delete");
        MemoBox.Text = sticker.Text;
        MemoBox.Focus();
        MemoBox.CaretIndex = MemoBox.Text.Length;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _sticker.Text = MemoBox.Text.Trim();
        _store.Save();
        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        Deleted = true;
        _store.Remove(_sticker);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
