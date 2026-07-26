using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using LeeYongeOrdering.Services;

namespace LeeYongeOrdering.Views;

/// <summary>
/// Three-column mapping panel for the Measurement Terms system:
/// left = garment types, center = the measurements assigned to the selected garment
/// (a drag-and-drop drop target), right = every available measurement. Predefined
/// terms/garments are locked; user-added ones can be renamed (inline + per-language),
/// deleted, and dragged into a garment's assigned list.
/// </summary>
public partial class MeasurementTermsWindow : Window
{
    private const string TermDragFormat = "MeasurementTermId";

    private readonly MeasurementTermsService _service = MeasurementTermsService.Instance;
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

        GarmentList.ItemsSource = _garmentRows;
        AllTermsList.ItemsSource = _filteredTermRows;
        AssignedList.ItemsSource = _assignedRows;

        // Set here (not via XAML IsChecked="True") so the Checked handler only runs once
        // every field in this window has been assigned by InitializeComponent — setting it
        // in XAML fires the event mid-parse, before the sibling Male/Female radios exist.
        GenderFilterAllRadio.IsChecked = true;

        RefreshGarmentRows();
        RefreshTermRows();

        if (_garmentRows.Count > 0)
            GarmentList.SelectedIndex = 0;
    }

    private static string CurrentLanguage => LocalizationService.Instance.CurrentLanguageCode;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Reads the XAML-generated GarmentList x:Name field, which SonarLint's single-file analysis cannot resolve.")]
    private GarmentRow? SelectedGarment => GarmentList.SelectedItem as GarmentRow;

    // --- List refresh -----------------------------------------------------------

    private void RefreshGarmentRows()
    {
        var previousId = SelectedGarment?.Garment.Id;

        _garmentRows.Clear();
        foreach (var garment in _service.Garments)
            _garmentRows.Add(new GarmentRow(garment, MeasurementTermsService.ResolveGarmentName(garment, CurrentLanguage)));

        var restore = _garmentRows.FirstOrDefault(row => string.Equals(row.Garment.Id, previousId, StringComparison.Ordinal))
                      ?? _garmentRows.FirstOrDefault();
        GarmentList.SelectedItem = restore;
    }

    private void RefreshTermRows()
    {
        _termRows.Clear();
        foreach (var term in _service.Terms)
            _termRows.Add(new TermRow(term, MeasurementTermsService.ResolveTermName(term, CurrentLanguage)));

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
            AssignedHintText.Text = LocalizationService.Instance["MeasureTerms.EmptyGarment"];
            AssignedHintPanel.Visibility = Visibility.Visible;
            MeasurementModePanel.Visibility = Visibility.Collapsed;
            return;
        }

        AssignedGarmentText.Text = MeasurementTermsService.ResolveGarmentName(garment, CurrentLanguage);
        RefreshMeasurementModePanel(garment);

        foreach (var term in _service.GetGarmentTerms(garment.Id))
        {
            var locked = MeasurementTermDefaults.IsTermLockedInGarment(garment, term.Id);
            _assignedRows.Add(new AssignedRow(term.Id, MeasurementTermsService.ResolveTermName(term, CurrentLanguage), locked));
        }

        AssignedHintText.Text = LocalizationService.Instance["MeasureTerms.DragHint"];
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
        MeasurementModeStatusText.Text = LocalizationService.Instance[
            isCustomized ? "MeasureTerms.CustomizedStatus" : "MeasureTerms.DefaultStatus"];
        MeasurementModeActionButton.Content = LocalizationService.Instance[
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
                LocalizationService.Instance["MeasureTerms.RestoreDefaultConfirm"],
                LocalizationService.Instance["MeasureTerms.DeleteTitle"],
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
            LocalizationService.Instance["MeasureTerms.AddProp"],
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

    private void ShowDuplicateTermWarning()
    {
        MessageBox.Show(
            LocalizationService.Instance["MeasureTerms.DuplicateTermWarning"],
            LocalizationService.Instance["MeasureTerms.Title"],
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

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
                LocalizationService.Instance["MeasureTerms.NameRequired"],
                LocalizationService.Instance["MeasureTerms.Title"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var names = new Dictionary<string, string>(row.Term.Names) { [CurrentLanguage] = newName };
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
            MeasurementTermsService.ResolveTermName(row.Term, CurrentLanguage), row.Term.Names, row.Term.Gender)
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
            LocalizationService.Instance["MeasureTerms.DeleteTermConfirm"],
            LocalizationService.Instance["MeasureTerms.DeleteTitle"],
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
            LocalizationService.Instance["MeasureTerms.AddGarment"],
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
            MeasurementTermsService.ResolveGarmentName(row.Garment, CurrentLanguage), row.Garment.Names)
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
            LocalizationService.Instance["MeasureTerms.DeleteGarmentConfirm"],
            LocalizationService.Instance["MeasureTerms.DeleteTitle"],
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

        public TermRow(MeasurementTerm term, string name)
        {
            Term = term;
            Name = name;
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

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (gender badge text); not visible to single-file analysis.")]
        public string GenderGlyph => Term.Gender switch
        {
            MeasurementGender.Male => "\u2642",
            MeasurementGender.Female => "\u2640",
            _ => string.Empty
        };

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed",
            Justification = "Bound in MeasurementTermsWindow.xaml (gender badge tooltip); not visible to single-file analysis.")]
        public string GenderTooltip => Term.Gender switch
        {
            MeasurementGender.Male => LocalizationService.Instance["MeasureTerms.FilterMale"],
            MeasurementGender.Female => LocalizationService.Instance["MeasureTerms.FilterFemale"],
            _ => string.Empty
        };

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
