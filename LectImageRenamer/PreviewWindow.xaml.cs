using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using LectImageRenamer.Models;

namespace LectImageRenamer;

public partial class PreviewWindow : Window, INotifyPropertyChanged
{
    private BitmapImage? _previewImage;
    private string _previewPath = string.Empty;
    private ImageItem? _currentImage;

    public static readonly DependencyProperty IsCommentModeProperty = DependencyProperty.Register(
        nameof(IsCommentMode), typeof(bool), typeof(PreviewWindow), new PropertyMetadata(false, (sender, args) =>
        {
            var window = (PreviewWindow)sender;
            // Add the editor below the existing image area when the window can grow.
            if (window.WindowState == WindowState.Normal)
                window.Height = Math.Max(window.MinHeight, window.Height + ((bool)args.NewValue ? 96 : -96));
        }));

    public bool IsCommentMode
    {
        get => (bool)GetValue(IsCommentModeProperty);
        set => SetValue(IsCommentModeProperty, value);
    }

    public ImageItem? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (_currentImage is not null) _currentImage.PropertyChanged -= OnImageChanged;
            _currentImage = value;
            if (_currentImage is not null) _currentImage.PropertyChanged += OnImageChanged;
            OnPropertyChanged();
        }
    }

    private void OnImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ImageItem.FileName) || CurrentImage is null) return;
        PreviewPath = CurrentImage.FullPath;
        Title = $"プレビュー - {CurrentImage.FileName}";
    }

    public PreviewWindow()
    {
        InitializeComponent();
        DataContext = this;
        Closed += (_, _) =>
        {
            CurrentImage?.CommentDocument?.Flush();
            CurrentImage = null;
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set
        {
            _previewImage = value;
            OnPropertyChanged();
        }
    }

    public string PreviewPath
    {
        get => _previewPath;
        private set
        {
            _previewPath = value;
            OnPropertyChanged();
        }
    }

    public void ShowImage(ImageItem item)
    {
        if (!File.Exists(item.FullPath))
        {
            ClearImage();
            return;
        }

        PreviewImage = ImageItem.LoadBitmap(item.FullPath);
        CurrentImage = item;
        PreviewPath = item.FullPath;
        Title = $"プレビュー - {item.FileName}";

        bool wasVisible = IsVisible;
        if (!wasVisible)
        {
            Show();
            Activate();
        }
    }

    public void ClearImage()
    {
        PreviewImage = null;
        CurrentImage = null;
        PreviewPath = string.Empty;
        Title = "プレビュー";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
