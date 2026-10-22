using System.ComponentModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Threading;

namespace LectImageRenamer.Models;

// One small, human-readable JSON document per image directory.
public sealed class FolderComments : INotifyPropertyChanged
{
    public const string FileName = "image-comments.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly Dictionary<string, CommentRecord> _comments = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _dirty;
    private string _status = "自動保存";

    public FolderComments(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
        _saveTimer.Tick += (_, _) => Flush();
        try
        {
            if (File.Exists(FilePath))
            {
                var records = JsonSerializer.Deserialize<List<CommentRecord>>(File.ReadAllText(FilePath), JsonOptions)
                    ?? throw new JsonException("コメントの配列がありません。");
                foreach (var record in records)
                {
                    if (record is null || string.IsNullOrEmpty(record.FileName) || record.Comment is null)
                        throw new JsonException("コメントの形式が正しくありません。");
                    _comments[record.FileName] = record;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CanEdit = false;
            Status = $"読込失敗: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string FilePath { get; }
    public bool CanEdit { get; } = true;
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public string GetComment(string fileName) => _comments.GetValueOrDefault(fileName)?.Comment ?? string.Empty;

    public void Update(string fileName, string comment)
    {
        if (!CanEdit) return;
        if (string.IsNullOrWhiteSpace(comment)) _comments.Remove(fileName);
        else _comments[fileName] = new CommentRecord(fileName, default, comment);
        _changed.Add(fileName);
        ScheduleSave();
    }

    public void Rename(IReadOnlyList<(string OldName, string NewName)> names)
    {
        if (!CanEdit) return;
        var moved = names.Select(pair => (pair.NewName, Record: _comments.GetValueOrDefault(pair.OldName))).ToList();
        bool affected = names.Any(pair => _comments.ContainsKey(pair.OldName) || _comments.ContainsKey(pair.NewName));
        foreach (var pair in names) _comments.Remove(pair.OldName);
        foreach (var pair in moved)
        {
            _comments.Remove(pair.NewName);
            if (pair.Record is not null) _comments[pair.NewName] = pair.Record with { FileName = pair.NewName };
        }
        if (affected) ScheduleSave();
    }

    private void ScheduleSave()
    {
        _dirty = true;
        Status = "未保存…";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public bool Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return true;
        try
        {
            var now = DateTimeOffset.Now;
            var records = _comments.Values.OrderBy(record => record.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(record => _changed.Contains(record.FileName) ? record with { SavedAt = now } : record).ToList();
            // Replace only after the complete JSON has been written.
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
            foreach (var record in records) _comments[record.FileName] = record;
            _changed.Clear();
            _dirty = false;
            Status = "保存済み";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"保存失敗: {ex.Message}";
            return false;
        }
    }

    public sealed record CommentRecord(string FileName, DateTimeOffset SavedAt, string Comment);
}
