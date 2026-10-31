using System.ComponentModel;
using System.Runtime.CompilerServices;
using SimplerFileBot.Core;

namespace SimplerFileBot.WinUI;

public sealed class PreviewRow(MediaItem item) : INotifyPropertyChanged
{
    public MediaItem Item { get; } = item;
    public string OriginalName => Path.GetFileName(Item.SourcePath);
    private string _proposedName = "";
    private string _status = "Match needed";
    private string _displayTitle = item.Title;
    private bool _selected = true;
    public string ProposedName { get => _proposedName; set { _proposedName = value; Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string DisplayTitle { get => _displayTitle; set { _displayTitle = value; Changed(); } }
    public bool IsSelected { get => _selected; set { _selected = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void RefreshOriginal() => Changed(nameof(OriginalName));
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
