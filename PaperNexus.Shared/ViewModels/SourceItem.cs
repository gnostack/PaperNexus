using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PaperNexus.ViewModels;

// One row of a sources list, on either device: the source's fields, its on/off switch, and its
// edit and delete buttons. The list that owns the row passes in what each control does, so the
// row holds no reference to a view model or a service.
public partial class SourceItem : ObservableObject
{
    private readonly Action<SourceItem> _edit;
    private readonly Func<SourceItem, Task> _delete;
    private readonly Func<SourceItem, bool, Task> _setEnabled;

    // True while the row's own fields are being refreshed from the saved list, so the switch
    // moving to the saved value is not taken as the person flipping it.
    private bool _applying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(Url))]
    [NotifyPropertyChangedFor(nameof(CronExpression))]
    private SourceFields _fields;

    [ObservableProperty]
    private bool _isEnabled;

    // True while one of this row's actions runs. The phone dims the row's controls meanwhile.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _isBusy;

    public SourceItem(
        SourceFields fields,
        Action<SourceItem> edit,
        Func<SourceItem, Task> delete,
        Func<SourceItem, bool, Task> setEnabled)
    {
        _fields = fields.ThrowIfNull();
        _isEnabled = fields.IsEnabled;
        _edit = edit.ThrowIfNull();
        _delete = delete.ThrowIfNull();
        _setEnabled = setEnabled.ThrowIfNull();
    }

    public string Name => Fields.Name;

    public string Url => Fields.Url;

    public string CronExpression => Fields.CronExpression;

    public bool CanAct => !IsBusy;

    // Shows the saved fields, including the saved on/off state, without saving anything.
    public void Apply(SourceFields fields)
    {
        _applying = true;
        try
        {
            Fields = fields.ThrowIfNull();
            IsEnabled = fields.IsEnabled;
        }
        finally
        {
            _applying = false;
        }
    }

    // The person flipped the switch: the list saves it at once. A failed save puts the switch
    // back (the list calls Apply).
    partial void OnIsEnabledChanged(bool value)
    {
        if (_applying)
            return;
        _ = RunAsync(item => _setEnabled(item, value));
    }

    [RelayCommand]
    private void Edit() => _edit(this);

    [RelayCommand]
    private Task Delete() => RunAsync(_delete);

    private async Task RunAsync(Func<SourceItem, Task> action)
    {
        IsBusy = true;
        try
        {
            await action(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Brings the rows into the order and values of the saved list, reusing the row already shown
    // for a name (so the desktop's selection and the phone's place in the list stay), creating
    // rows for new sources and removing the rest.
    public static void Reconcile(
        ObservableCollection<SourceItem> items,
        IReadOnlyList<SourceFields> sources,
        Func<SourceFields, SourceItem> create)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            var fields = sources[index];
            var existing = -1;
            for (var at = index; at < items.Count; at++)
            {
                if (string.Equals(items[at].Name, fields.Name, StringComparison.OrdinalIgnoreCase))
                {
                    existing = at;
                    break;
                }
            }

            if (existing < 0)
            {
                items.Insert(index, create(fields));
                continue;
            }

            if (existing != index)
                items.Move(existing, index);
            items[index].Apply(fields);
        }

        while (items.Count > sources.Count)
            items.RemoveAt(items.Count - 1);
    }
}
