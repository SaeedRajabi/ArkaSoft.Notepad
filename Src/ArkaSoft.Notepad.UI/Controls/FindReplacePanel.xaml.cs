using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArkaSoft.Notepad.UI.Services;

namespace ArkaSoft.Notepad.UI.Controls;

/// <summary>
/// The floating find/replace card. The owner (MainWindow) supplies the
/// search behavior through the callbacks; the panel only manages UI state.
/// </summary>
public partial class FindReplacePanel : UserControl
{
    private string _lastCountText = string.Empty;

    public FindReplacePanel()
    {
        InitializeComponent();
    }

    /// <summary>Owner-implemented actions. All must be set before the panel is used.</summary>
    public Action? FindNext { get; set; }
    public Action? FindPrevious { get; set; }
    public Action? ReplaceOne { get; set; }
    public Action? ReplaceAll { get; set; }
    public Action? CloseRequested { get; set; }
    public Action? OptionsChanged { get; set; }

    public string SearchText => SearchBox.Text;
    public string ReplaceText => ReplaceBox.Text;
    public bool MatchCase => CaseToggle.IsChecked == true;
    public bool IsReplaceMode => ReplaceToggle.IsChecked == true;

    public void Open(bool withReplace, string initialTerm)
    {
        Visibility = Visibility.Visible;
        if (!string.IsNullOrEmpty(initialTerm))
            SearchBox.Text = initialTerm;
        if (withReplace)
            ReplaceToggle.IsChecked = true;
        SearchBox.SelectAll();
        SearchBox.Focus();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        ReplaceToggle.IsChecked = false;
    }

    public void UpdateCount(int current, int total)
    {
        var text = total > 0 ? $"{current}/{total}" : LocalizationService.Get("NoResults");
        if (text != _lastCountText)
        {
            _lastCountText = text;
            MatchCount.Text = text;
        }
    }

    public void ShowNote(string note)
    {
        _lastCountText = note;
        MatchCount.Text = note;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => OptionsChanged?.Invoke();

    private void CaseToggle_Changed(object sender, RoutedEventArgs e)
        => OptionsChanged?.Invoke();

    private void ReplaceToggle_Changed(object sender, RoutedEventArgs e)
    {
        ReplaceRow.Visibility = ReplaceToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                FindPrevious?.Invoke();
            else
                FindNext?.Invoke();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        }
    }

    private void ReplaceBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ReplaceOne?.Invoke();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => FindPrevious?.Invoke();

    private void NextButton_Click(object sender, RoutedEventArgs e) => FindNext?.Invoke();

    private void ReplaceOneButton_Click(object sender, RoutedEventArgs e) => ReplaceOne?.Invoke();

    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => ReplaceAll?.Invoke();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
