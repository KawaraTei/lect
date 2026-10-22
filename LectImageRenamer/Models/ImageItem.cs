using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace LectImageRenamer.Models;

public sealed class ImageItem : INotifyPropertyChanged
{
    private string _fullPath;
    private string _fileName;
    private int _displayIndex;
    private bool _isSelected;
    private string _comment = string.Empty;

    public FolderComments? CommentDocument { get; init; }

    public string Comment
    {
        get => _comment;
        set
        {
            if (SetField(ref _comment, value)) CommentDocument?.Update(FileName, value);
        }
    }

    public void LoadComment() => _comment = CommentDocument?.GetComment(FileName) ?? string.Empty;

    public ImageItem(string fullPath)
    {
        _fullPath = fullPath;
        _fileName = Path.GetFileName(fullPath);
        Thumbnail = LoadBitmap(fullPath, decodePixelWidth: 360);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FullPath
    {
        get => _fullPath;
        private set => SetField(ref _fullPath, value);
    }

    public string FileName
    {
        get => _fileName;
        private set => SetField(ref _fileName, value);
    }

    public string Extension => Path.GetExtension(FullPath);

    public int DisplayIndex
    {
        get => _displayIndex;
        set => SetField(ref _displayIndex, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public BitmapImage Thumbnail { get; }

    public void ApplyRenamedPath(string newPath)
    {
        FullPath = newPath;
        FileName = Path.GetFileName(newPath);
        OnPropertyChanged(nameof(Extension));
    }

    public static BitmapImage LoadBitmap(string path, int? decodePixelWidth = null)
    {
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth.HasValue)
        {
            bitmap.DecodePixelWidth = decodePixelWidth.Value;
        }

        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
