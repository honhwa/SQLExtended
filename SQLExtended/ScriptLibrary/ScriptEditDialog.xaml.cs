using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SQLExtended.ScriptLibrary.Models;
using System.Windows;
using System.Windows.Input;
using System.Xml;

namespace SQLExtended.ScriptLibrary;

/// <summary>
/// Add/edit dialog for a user script. <see cref="Result"/> holds the saved values when the dialog returns true.
/// </summary>
public partial class ScriptEditDialog : Window
{
    private readonly LibraryScript _source;

    public LibraryScript Result { get; private set; }

    public ScriptEditDialog(LibraryScript script)
    {
        InitializeComponent();
        InitializeSyntaxHighlighting();

        _source = script ?? new LibraryScript();
        NameBox.Text = _source.Name ?? "";
        CategoryBox.Text = string.IsNullOrWhiteSpace(_source.Category) ? "General" : _source.Category;
        DescriptionBox.Text = _source.Description ?? "";
        BodyEditor.Text = _source.Body ?? "";

        Title = string.IsNullOrEmpty(_source.Id) ? "New Script" : "Edit Script";
        Loaded += (s, e) => NameBox.Focus();
    }

    private void InitializeSyntaxHighlighting()
    {
        try
        {
            var assembly = typeof(ScriptEditDialog).Assembly;
            using var stream = assembly.GetManifestResourceStream("SQLExtended.Search.TsqlDarkHighlighting.xshd");
            if (stream == null) return;
            using var reader = new XmlTextReader(stream);
            BodyEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch { }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a name.", "SQLExtended Script Library", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        Result = new LibraryScript
        {
            Id = _source.Id,                       // preserved for edit; empty for new (service assigns a GUID)
            Name = name,
            Category = string.IsNullOrWhiteSpace(CategoryBox.Text) ? "General" : CategoryBox.Text.Trim(),
            Description = DescriptionBox.Text?.Trim() ?? "",
            Body = BodyEditor.Text ?? "",
            IsBuiltIn = false
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
