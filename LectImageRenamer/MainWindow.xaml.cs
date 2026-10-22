using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using LectImageRenamer.Models;
using LectImageRenamer.ViewModels;

namespace LectImageRenamer;

public partial class MainWindow : Window
{
    private const string ImageItemsDragFormat = "LectImageRenamer.ImageItems";

    private readonly MainWindowViewModel _viewModel;
    private Point _dragStartPoint;
    private ImageItem? _dragStartItem;
    private PreviewWindow? _previewWindow;
    private bool _suppressPreviewUpdates;
    private bool _preservedMultiSelectionForDrag;
    private bool _dragStarted;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        _viewModel.ImagesDeleting += OnImagesDeleting;
        _viewModel.ImagesDeleted += OnImagesDeleted;
        _viewModel.ImagesCleared += OnImagesCleared;
        DataContext = _viewModel;
        Closing += OnClosing;
        Closed += (_, _) => _previewWindow?.Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.FlushComments())
        {
            e.Cancel = MessageBox.Show("コメントを保存できませんでした。保存せず終了しますか？", "コメント",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes;
        }
    }

    private void OnImagesDeleting(object? sender, EventArgs e)
    {
        _suppressPreviewUpdates = true;
        _previewWindow?.ClearImage();
    }

    private void OnImagesDeleted(object? sender, EventArgs e)
    {
        _suppressPreviewUpdates = false;
    }

    private void OnImagesCleared(object? sender, EventArgs e)
    {
        _previewWindow?.ClearImage();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTextInputElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (TryHandleGridHorizontalNavigation(e))
        {
            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        if (_viewModel.DeleteSelectedCommand.CanExecute(null))
        {
            _viewModel.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.NotifySelectionChanged();

        if (_suppressPreviewUpdates)
        {
            return;
        }

        IReadOnlyList<ImageItem> selectedItems = _viewModel.SelectedItems;
        if (selectedItems.Count != 1)
        {
            return;
        }

        if (_previewWindow is null)
        {
            _previewWindow = new PreviewWindow();
            _previewWindow.SetBinding(PreviewWindow.IsCommentModeProperty,
                new System.Windows.Data.Binding(nameof(MainWindowViewModel.IsCommentMode)) { Source = _viewModel, Mode = System.Windows.Data.BindingMode.TwoWay });
            _previewWindow.Closed += (_, _) => _previewWindow = null;
        }
        _previewWindow.ShowImage(selectedItems[0]);
    }

    private void ImageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragStartItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as ImageItem;
        _preservedMultiSelectionForDrag = false;
        _dragStarted = false;

        if (IsTextInputElement(e.OriginalSource as DependencyObject))
        {
            _dragStartItem = null;
            return;
        }

        if (_dragStartItem is not null &&
            _dragStartItem.IsSelected &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _dragStartItem.IsSelected = false;
            _viewModel.NotifySelectionChanged();
            _dragStartItem = null;
            e.Handled = true;
            return;
        }

        if (_dragStartItem is not null &&
            _dragStartItem.IsSelected &&
            _viewModel.SelectedItems.Count > 1)
        {
            _preservedMultiSelectionForDrag = true;
            e.Handled = true;
        }
    }

    private void ImageList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_preservedMultiSelectionForDrag)
        {
            return;
        }

        if (!_dragStarted && _dragStartItem is not null)
        {
            SelectSingleImage(_dragStartItem);
        }

        _preservedMultiSelectionForDrag = false;
        _dragStartItem = null;
        e.Handled = true;
    }

    private void ImageList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartItem is null)
        {
            return;
        }

        Point currentPoint = e.GetPosition(null);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        List<ImageItem> movingItems = _dragStartItem.IsSelected
            ? _viewModel.SelectedItems.ToList()
            : [_dragStartItem];

        DataObject dataObject = new();
        dataObject.SetData(ImageItemsDragFormat, movingItems);
        _dragStarted = true;
        DragDrop.DoDragDrop((DependencyObject)sender, dataObject, DragDropEffects.Move);
        _dragStartItem = null;
        _preservedMultiSelectionForDrag = false;
    }

    private void ImageList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(ImageItemsDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void ImageList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddPaths(paths);
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(ImageItemsDragFormat) &&
            e.Data.GetData(ImageItemsDragFormat) is IReadOnlyList<ImageItem> movingItems)
        {
            ImageItem? targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as ImageItem;
            int targetIndex = targetItem is null ? _viewModel.Images.Count - 1 : _viewModel.Images.IndexOf(targetItem);
            _viewModel.MoveItems(movingItems, targetIndex);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? dependencyObject)
        where T : DependencyObject
    {
        while (dependencyObject is not null)
        {
            if (dependencyObject is T ancestor)
            {
                return ancestor;
            }

            dependencyObject = System.Windows.Media.VisualTreeHelper.GetParent(dependencyObject);
        }

        return null;
    }

    private static bool IsTextInputElement(DependencyObject? dependencyObject)
    {
        return FindAncestor<TextBox>(dependencyObject) is not null ||
               FindAncestor<PasswordBox>(dependencyObject) is not null ||
               FindAncestor<RichTextBox>(dependencyObject) is not null;
    }

    private bool TryHandleGridHorizontalNavigation(KeyEventArgs e)
    {
        if (!_viewModel.ShowGrid ||
            e.Key is not Key.Left and not Key.Right ||
            FindAncestor<ListBox>(e.OriginalSource as DependencyObject) != GridImageList ||
            _viewModel.Images.Count == 0)
        {
            return false;
        }

        int currentIndex = GridImageList.SelectedIndex;
        if (currentIndex < 0)
        {
            SelectImageAt(0);
            e.Handled = true;
            return true;
        }

        int nextIndex = e.Key == Key.Right ? currentIndex + 1 : currentIndex - 1;
        if (nextIndex < 0 || nextIndex >= _viewModel.Images.Count)
        {
            e.Handled = true;
            return true;
        }

        SelectImageAt(nextIndex);
        e.Handled = true;
        return true;
    }

    private void SelectImageAt(int index)
    {
        SelectSingleImage(_viewModel.Images[index]);
        GridImageList.ScrollIntoView(_viewModel.Images[index]);
        ListImageList.ScrollIntoView(_viewModel.Images[index]);
        FocusSelectedGridItem(_viewModel.Images[index]);
    }

    private void SelectSingleImage(ImageItem selectedImage)
    {
        foreach (ImageItem image in _viewModel.Images)
        {
            image.IsSelected = ReferenceEquals(image, selectedImage);
        }

        _viewModel.NotifySelectionChanged();
    }

    private void FocusSelectedGridItem(ImageItem selectedImage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (GridImageList.ItemContainerGenerator.ContainerFromItem(selectedImage) is ListBoxItem item)
            {
                item.Focus();
            }
            else
            {
                GridImageList.Focus();
            }
        }, DispatcherPriority.Input);
    }
}
