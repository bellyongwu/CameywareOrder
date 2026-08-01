using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;

namespace CameywareOrder.Views;

/// <summary>
/// Three-column mapping panel for the Measurement Terms system:
/// left = garment types, center = the measurements assigned to the selected garment
/// (a drag-and-drop drop target), right = every available measurement. Predefined
/// terms/garments are locked; user-added ones can be renamed (inline + per-language),
/// deleted, and dragged into a garment's assigned list.
/// </summary>
/// <remarks>
/// <b>The panel renders in a language of its OWN</b> (<see cref="LocalizationScope"/>, declared in
/// the XAML and driven by the picker in the header), so a translation can be checked without moving
/// the whole application into that language and back. Term and garment names resolve against the
/// same scope, which is the point: the names are the thing being checked.
///
/// The split is between DISPLAY and INSTRUCTION. Anything describing the terms — labels, the title,
/// every name — follows the preview. Anything the user has to act on — the confirmation dialogs, the
/// warnings, the picker's own label — stays in the application's language, because a "delete this?"
/// prompt in a language the reader picked precisely BECAUSE they cannot read it fluently is a trap.
/// Same rule the selector control itself follows.
///
/// One thing deliberately follows the preview rather than the application: an inline rename writes
/// the new name into the language being PREVIEWED. Renaming while looking at Japanese means editing
/// the Japanese name, which is both what it looks like and what makes this screen usable for
/// filling translation gaps.
/// </remarks>
public partial class MeasurementTermsWindow : Window
{
    private const string TermDragFormat = "MeasurementTermId";

    private readonly MeasurementTermsService _service = MeasurementTermsService.Instance;

    /// <summary>
    /// The language this panel is being read in. Taken from the XAML resource rather than built here
    /// so the markup's bindings and this code are looking at the same object — two scopes would let
    /// the labels and the names drift into different languages.
    /// </summary>
    private readonly LocalizationScope _scope;

    /// <summary>What a dialog or a warning is written in — the reader's own language, never the preview.</summary>
    private readonly LocalizationService _localization = LocalizationService.Instance;

    private readonly ObservableCollection<GarmentRow> _garmentRows = new();
    private readonly ObservableCollection<TermRow> _termRows = new();
    private readonly ObservableCollection<TermRow> _filteredTermRows = new();
    private readonly ObservableCollection<AssignedRow> _assignedRows = new();

    private MeasurementGender? _termGenderFilter;
    private string _termSearchText = string.Empty;

    private Point _dragStartPoint;
    private TermRow? _dragCandidate;

    public MeasurementTermsWindow()
    {
        InitializeComponent();

        _scope = (LocalizationScope)Resources["Scope"];
        // The rows carry names resolved in code, so a binding refresh cannot reach them — they have
        // to be rebuilt when the preview moves.
        _scope.TextChanged += OnPreviewLanguageChanged;

        GarmentList.ItemsSource = _garmentRows;
        AllTermsList.ItemsSource = _filteredTermRows;
        AssignedList.ItemsSource = _assignedRows;

        // Set here (not via XAML IsChecked="True") so the Checked handler only runs once
        // every field in this window has been assigned by InitializeComponent — setting it
        // in XAML fires the event mid-parse, before the sibling Male/Female radios exist.
        GenderFilterAllRadio.IsChecked = true;

        RefreshTitle();
        RefreshGarmentRows();
        RefreshTermRows();

        if (_garmentRows.Count > 0)
            GarmentList.SelectedIndex = 0;
    }

    /// <summary>The language the panel is being READ in — the preview, not the application's.</summary>
    private string PreviewLanguage => _scope.EffectiveLanguageCode;

    /// <summary>
    /// Set in code because a <c>Window</c>'s own properties are assigned before its
    /// <c>Resources</c> exist, so the scope cannot be reached from a Title binding in the markup.
    /// </summary>
    private void RefreshTitle() => Title = _scope["MeasureTerms.Title"];

    private void OnPreviewLanguageChanged(object? sender, EventArgs e)
    {
        RefreshTitle();
        RefreshGarmentRows();
        RefreshTermRows();
        RefreshAssigned();
    }

    /// <summary>
    /// Drops the scope's subscription to the localization singleton. Without it the singleton holds
    /// this window alive for the life of the process — see <see cref="LocalizationScope.Detach"/>.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _scope.TextChanged -= OnPreviewLanguageChanged;
        _scope.Detach();
        base.OnClosed(e);
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Reads the XAML-generated GarmentList x:Name field, which SonarLint's single-file analysis cannot resolve.")]
    private GarmentRow? SelectedGarment => GarmentList.SelectedItem as GarmentRow;

    // --- List refresh -----------------------------------------------------------

    private void RefreshGarmentRows()
    {
        var previousId = SelectedGarment?.Garment.Id;

        _garmentRows.Clear();
        foreach (var garment in _service.Garments)
            _garmentRows.Add(new GarmentRow(garment, MeasurementTermsService.ResolveGarmentName(garment, PreviewLanguage)));

        var restore = _garmentRows.FirstOrDefault(row => string.Equals(row.Garment.Id, previousId, StringComparison.Ordinal))
                      ?? _garmentRows.FirstOrDefault();
        GarmentList.SelectedItem = restore;
    }

    private void RefreshTermRows()
    {
        _termRows.Clear();
        foreach (var term in _service.Terms)
            _termRows.Add(new TermRow(term, MeasurementTermsService.ResolveTermName(term, PreviewLanguage), _scope));

        ApplyTermFilter();
    }

    // Common terms always show (they apply to every garment); Male/Female narrows the
    // list to that gender's specific terms in addition to the common ones. The search
    // box further narrows by name within whatever gender filter is active.
    private void ApplyTermFilter()
    {
        _filteredTermRows.Clear();

        var query = _termSearchText.Trim();
        foreach (var row in _termRows)
        {
            if (_termGenderFilter.HasValue
                && row.Term.Gender != MeasurementGender.Common
                && row.Term.Gender != _termGenderFilter.Value)
            {
                continue;
            }

            if (query.Length > 0 && row.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
                continue;

            _filteredTermRows.Add(row);
        }
    }

    private void OnTermGenderFilterChanged(object sender, RoutedEventArgs e)
    {
        if (GenderFilterMaleRadio.IsChecked is true)
            _termGenderFilter = MeasurementGender.Male;
        else if (GenderFilterFemaleRadio.IsChecked is true)
            _termGenderFilter = MeasurementGender.Female;
        else
            _termGenderFilter = null;

        ApplyTermFilter();
    }

    private void OnTermSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _termSearchText = AllTermsSearchBox.Text;
        AllTermsSearchHint.Visibility = string.IsNullOrEmpty(_termSearchText) ? Visibility.Visible : Visibility.Collapsed;
        ApplyTermFilter();
    }

    private void RefreshAssigned()
    {
        _assignedRows.Clear();

        var garment = SelectedGarment?.Garment;
        if (garment is null)
        {
            AssignedGarmentText.Text = string.Empty;
            AssignedHintText.Text = _scope["MeasureTerms.EmptyGarment"];
            AssignedHintPanel.Visibility = Visibility.Visible;
            MeasurementModePanel.Visibility = Visibility.Collapsed;
            return;
        }

        AssignedGarmentText.Text = MeasurementTermsService.ResolveGarmentName(garment, PreviewLanguage);
        RefreshMeasurementModePanel(garment);

        foreach (var term in _service.GetGarmentTerms(garment.Id))
        {
            var locked = MeasurementTermDefaults.IsTermLockedInGarment(garment, term.Id);
            _assignedRows.Add(new AssignedRow(term.Id, MeasurementTermsService.ResolveTermName(term, PreviewLanguage), locked));
        }

        AssignedHintText.Text = _scope["MeasureTerms.DragHint"];
        AssignedHintPanel.Visibility = _assignedRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Only predefined garments have a locked default set to opt out of; user-added
    // garments are always fully editable, so the mode switch stays hidden for them.
    private void RefreshMeasurementModePanel(GarmentType garment)
    {
        if (!garment.IsPredefined)
        {
            MeasurementModePanel.Visibility = Visibility.Collapsed;
            return;
        }

        MeasurementModePanel.Visibility = Visibility.Visible;
        var isCustomized = garment.UseCustomMeasurements;
        MeasurementModeStatusText.Text = _scope[
            isCustomized ? "MeasureTerms.CustomizedStatus" : "MeasureTerms.DefaultStatus"];
        MeasurementModeActionButton.Content = _scope[
            isCustomized ? "MeasureTerms.RestoreDefault" : "MeasureTerms.Customize"];
    }

    private void OnMeasurementModeActionClick(object sender, RoutedEventArgs e)
    {
        var garment = SelectedGarment?.Garment;
        if (garment is null || !garment.IsPredefined)
            return;

        if (garment.UseCustomMeasurements)
        {
            var confirm = MessageBox.Show(
                _localization["MeasureTerms.RestoreDefaultConfirm"],
                _localization["MeasureTerms.DeleteTitle"],
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            _service.RestoreDefaultMeasurements(garment.Id);
        }
        else
        {
            _service.EnableCustomMeasurements(garment.Id);
        }

        RefreshAssigned();
    }

    private void OnGarmentSelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshAssigned();

    // --- Drag & drop (right column → center) ------------------------------------

    private void OnTermPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TermRow row })
            return;

        // Don't start a drag from the interactive controls (edit/save/delete buttons,
        // or the inline rename text box).
        if (row.IsEditing || IsWithin<ButtonBase>(e.OriginalSource) || IsWithin<TextBox>(e.OriginalSource))
        {
            _dragCandidate = null;
            return;
        }

        _dragStartPoint = e.GetPosition(null);
        _dragCandidate = row;
    }

    private void OnTermPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(TermDragFormat, _dragCandidate.Term.Id);
        _dragCandidate = null;
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }

    private void OnAssignedDragOver(object sender, DragEventArgs e)
    {
        e.Effects = SelectedGarment is not null && e.Data.GetDataPresent(TermDragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnAssignedDrop(object sender, DragEventArgs e)
    {
        var garment = SelectedGarment?.Garment;
        if (garment is null || e.Data.GetData(TermDragFormat) is not string termId)
            return;

        _service.AddTermToGarment(garment.Id, termId);
        RefreshAssigned();
    }

    private void OnAssignedRemoveClick(object sender, RoutedEventArgs e)
    {
        var garment = SelectedGarment?.Garment;
        if (garment is null || sender is not FrameworkElement { Tag: AssignedRow row })
            return;

        _service.RemoveTermFromGarment(garment.Id, row.TermId);
        RefreshAssigned();
    }

    // --- Custom term actions (right column) -------------------------------------

    private void OnAddPropClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MeasurementTermLanguageWindow(
            _localization["MeasureTerms.AddProp"],
            new Dictionary<string, string>(),
            MeasurementGender.Common)
        {
            Owner = this
        };

        if (!dialog.ShowDialog().GetValueOrDefault())
            return;

        if (_service.IsDuplicateTermName(dialog.Result))
        {
            ShowDuplicateTermWarning();
            return;
        }

        _service.AddCustomTerm(dialog.Result, dialog.GenderResult);
        RefreshTermRows();
    }

    // Static, and genuinely so: it reads no x:Name control and no field, only the localization
    // singleton. This is NOT the S2325 WPF false positive the other view helpers hit.
    private static void ShowDuplicateTermWarning()
    {
        MessageBox.Show(
            LocalizationService.Instance["MeasureTerms.DuplicateTermWarning"],
            LocalizationService.Instance["MeasureTerms.Title"],
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (Click=\"OnTermEditClick\"). The generated InitializeComponent wires it " +
                        "as this.OnTermEditClick, which does not compile against a static method.")]
    private void OnTermEditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TermRow row })
            row.BeginEdit();
    }

    private void OnTermSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TermRow row })
            return;

        var newName = row.EditName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(
                _localization["MeasureTerms.NameRequired"],
                _localization["MeasureTerms.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var names = new Dictionary<string, string>(row.Term.Names) { [PreviewLanguage] = newName };
        if (_service.IsDuplicateTermName(names, row.Term.Id))
        {
            ShowDuplicateTermWarning();
            return;
        }

        _service.UpdateCustomTermNames(row.Term.Id, names, row.Term.Gender);
        RefreshTermRows();
        RefreshAssigned();
    }

    private void OnTermAltLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TermRow row })
            return;

        var dialog = new MeasurementTermLanguageWindow(
            MeasurementTermsService.ResolveTermName(row.Term, PreviewLanguage), row.Term.Names, row.Term.Gender)
        {
            Owner = this
        };

        if (!dialog.ShowDialog().GetValueOrDefault())
            return;

        if (_service.IsDuplicateTermName(dialog.Result, row.Term.Id))
        {
            ShowDuplicateTermWarning();
            return;
        }

        _service.UpdateCustomTermNames(row.Term.Id, dialog.Result, dialog.GenderResult);
        RefreshTermRows();
        RefreshAssigned();
    }

    private void OnTermDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TermRow row })
            return;

        var confirm = MessageBox.Show(
            _localization["MeasureTerms.DeleteTermConfirm"],
            _localization["MeasureTerms.DeleteTitle"],
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _service.DeleteCustomTerm(row.Term.Id);
        RefreshTermRows();
        RefreshAssigned();
    }

    // --- Garment actions (left column) ------------------------------------------

    private void OnAddGarmentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MeasurementTermLanguageWindow(
            _localization["MeasureTerms.AddGarment"],
            new Dictionary<string, string>())
        {
            Owner = this
        };

        if (!dialog.ShowDialog().GetValueOrDefault())
            return;

        var created = _service.AddCustomGarment(dialog.Result);
        RefreshGarmentRows();
        GarmentList.SelectedItem = _garmentRows.FirstOrDefault(row => string.Equals(row.Garment.Id, created.Id, StringComparison.Ordinal));
    }

    private void OnGarmentAltLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GarmentRow row })
            return;

        var dialog = new MeasurementTermLanguageWindow(
            MeasurementTermsService.ResolveGarmentName(row.Garment, PreviewLanguage), row.Garment.Names)
        {
            Owner = this
        };

        if (dialog.ShowDialog().GetValueOrDefault())
        {
            _service.UpdateCustomGarmentNames(row.Garment.Id, dialog.Result);
            RefreshGarmentRows();
        }
    }

    private void OnGarmentDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GarmentRow row })
            return;

        var confirm = MessageBox.Show(
            _localization["MeasureTerms.DeleteGarmentConfirm"],
            _localization["MeasureTerms.DeleteTitle"],
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        _service.DeleteCustomGarment(row.Garment.Id);
        RefreshGarmentRows();
        RefreshAssigned();
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();

    private static bool IsWithin<T>(object? source) where T : DependencyObject
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is T)
                return true;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    // --- Row view-models --------------------------------------------------------

    private sealed class GarmentRow
    {
        public GarmentRow(GarmentType garment, string name)
        {
            Garment = garment;
            Name = name;
        }

        public GarmentType Garment { get; }

        public string Name { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (Visibility of the predefined/custom controls); not visible to single-file analysis.")]
        public bool IsPredefined => Garment.IsPredefined;

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (Visibility of the predefined/custom controls); not visible to single-file analysis.")]
        public bool IsCustom => !Garment.IsPredefined;
    }

    private sealed class TermRow : INotifyPropertyChanged
    {
        private bool _isEditing;
        private string _editName = string.Empty;

        private readonly ILocalizedText _text;

        /// <param name="text">
        /// The panel's scope, so the gender badge's tooltip is written in the language being
        /// previewed rather than the application's — it describes the term, and the term's own name
        /// beside it has already moved.
        /// </param>
        public TermRow(MeasurementTerm term, string name, ILocalizedText text)
        {
            Term = term;
            Name = name;
            _text = text;
        }

        public MeasurementTerm Term { get; }

        public string Name { get; private set; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (Visibility of the predefined/custom controls); not visible to single-file analysis.")]
        public bool IsPredefined => Term.IsPredefined;

        public bool IsCustom => !Term.IsPredefined;

        public bool ShowEditButton => IsCustom && !IsEditing;

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (gender badge visibility/tooltip); not visible to single-file analysis.")]
        public bool ShowGenderGlyph => Term.Gender != MeasurementGender.Common;

        // Both of these come from MeasurementGenderPresentation rather than from a switch here: the
        // term editor's gender picker shows the same marks for the same classifications, and two
        // private copies of a symbol table drift \u2014 leaving one screen labelling a term with a mark
        // that means something other than what the other screen says it means.
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (gender badge text); not visible to single-file analysis.")]
        public string GenderGlyph => MeasurementGenderPresentation.Symbol(Term.Gender);

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (gender badge tooltip); not visible to single-file analysis.")]
        public string GenderTooltip => Term.Gender == MeasurementGender.Common
            ? string.Empty
            : MeasurementGenderPresentation.NameText(_text, Term.Gender);

        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (_isEditing == value)
                    return;
                _isEditing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotEditing));
                OnPropertyChanged(nameof(ShowEditButton));
            }
        }

        public bool IsNotEditing => !IsEditing;

        public string EditName
        {
            get => _editName;
            set
            {
                if (_editName == value)
                    return;
                _editName = value;
                OnPropertyChanged();
            }
        }

        public void BeginEdit()
        {
            EditName = Name;
            IsEditing = true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class AssignedRow
    {
        public AssignedRow(string termId, string name, bool isLocked)
        {
            TermId = termId;
            Name = name;
            IsLocked = isLocked;
        }

        public string TermId { get; }

        public string Name { get; }

        public bool IsLocked { get; }

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (Visibility of the remove button); not visible to single-file analysis.")]
        public bool CanRemove => !IsLocked;
    }
}
