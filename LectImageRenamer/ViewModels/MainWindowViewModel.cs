using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using LectImageRenamer.Commands;
using LectImageRenamer.Models;
using Microsoft.VisualBasic.FileIO;

namespace LectImageRenamer.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
    };

    private bool _isGridView = true;
    private bool _isCommentMode;
    private readonly Dictionary<string, FolderComments> _commentDocuments = new(StringComparer.OrdinalIgnoreCase);
    private double _gridItemSize = 160;
    private string _renamePrefix = "image";
    private int _renameStartNumber = 1;
    private string _statusText = "画像をドラッグアンドドロップしてください。";

    public MainWindowViewModel()
    {
        RenameCommand = new RelayCommand(RenameAllImages, () => Images.Count > 0);
        DeleteSelectedCommand = new RelayCommand(DeleteSelectedImages, () => SelectedItems.Count > 0);
        ClearCommand = new RelayCommand(ClearImages, () => Images.Count > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ImagesDeleting;
    public event EventHandler? ImagesDeleted;
    public event EventHandler? ImagesCleared;

    public ObservableCollection<ImageItem> Images { get; } = new();

    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand ClearCommand { get; }

    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            if (SetField(ref _isGridView, value)) OnPropertyChanged(nameof(ShowGrid));
        }
    }

    public bool IsCommentMode
    {
        get => _isCommentMode;
        set
        {
            if (!SetField(ref _isCommentMode, value)) return;
            OnPropertyChanged(nameof(ShowGrid));
            OnPropertyChanged(nameof(CanChooseLayout));
            if (!value && !FlushComments()) StatusText = "コメントを保存できません。コメントモードで保存状態を確認してください。";
        }
    }

    public bool ShowGrid => IsCommentMode || IsGridView;
    public bool CanChooseLayout => !IsCommentMode;

    public bool FlushComments()
    {
        bool success = true;
        foreach (var document in _commentDocuments.Values) success &= document.Flush();
        return success;
    }

    public double GridItemSize
    {
        get => _gridItemSize;
        set => SetField(ref _gridItemSize, value);
    }

    public string RenamePrefix
    {
        get => _renamePrefix;
        set => SetField(ref _renamePrefix, value);
    }

    public int RenameStartNumber
    {
        get => _renameStartNumber;
        set => SetField(ref _renameStartNumber, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool HasNoImages => Images.Count == 0;

    public string DropHint => $"{Images.Count} 件 / 選択 {SelectedItems.Count} 件";

    public IReadOnlyList<ImageItem> SelectedItems => Images.Where(image => image.IsSelected).ToList();

    public void AddPaths(IEnumerable<string> paths)
    {
        List<string> candidates = paths.SelectMany(ExpandPath).ToList();
        HashSet<string> existing = Images.Select(image => image.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;
        int skipped = 0;

        foreach (string candidate in candidates)
        {
            if (!existing.Add(candidate))
            {
                skipped++;
                continue;
            }

            try
            {
                string directory = Path.GetDirectoryName(candidate)!;
                if (!_commentDocuments.TryGetValue(directory, out var document))
                {
                    document = new FolderComments(directory);
                    _commentDocuments.Add(directory, document);
                }
                var image = new ImageItem(candidate) { CommentDocument = document };
                image.LoadComment();
                Images.Add(image);
                added++;
            }
            catch
            {
                skipped++;
            }
        }

        RefreshIndexes();
        NotifyImageCountChanged();

        StatusText = added > 0
            ? $"{added} 件の画像を追加しました。{(skipped > 0 ? $"読み込み対象外または重複: {skipped} 件。" : string.Empty)}"
            : "追加できる画像がありませんでした。";
    }

    public void MoveItems(IReadOnlyList<ImageItem> movingItems, int targetIndex)
    {
        List<ImageItem> orderedMovingItems = Images.Where(movingItems.Contains).ToList();
        if (orderedMovingItems.Count == 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, Images.Count - 1);
        ImageItem targetItem = Images[targetIndex];
        if (orderedMovingItems.Contains(targetItem))
        {
            return;
        }

        foreach (ImageItem item in orderedMovingItems)
        {
            Images.Remove(item);
        }

        int insertIndex = Images.IndexOf(targetItem);
        if (insertIndex < 0)
        {
            insertIndex = Images.Count;
        }

        foreach (ImageItem item in orderedMovingItems)
        {
            Images.Insert(insertIndex, item);
            insertIndex++;
        }

        RefreshIndexes();
        NotifySelectionChanged();
        StatusText = $"{orderedMovingItems.Count} 件の並び順を変更しました。";
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedItems));
        OnPropertyChanged(nameof(DropHint));
        DeleteSelectedCommand.RaiseCanExecuteChanged();
    }

    private void ClearImages()
    {
        if (!FlushComments())
        {
            StatusText = "コメントを保存できません。一覧のクリアを中止しました。";
            return;
        }
        List<ImageItem> selectedItems = SelectedItems.ToList();
        if (selectedItems.Count > 0)
        {
            foreach (ImageItem item in selectedItems)
            {
                Images.Remove(item);
            }

            RefreshIndexes();
            StatusText = $"{selectedItems.Count} 件を一覧からクリアしました。";
        }
        else
        {
            Images.Clear();
            StatusText = "一覧をクリアしました。";
        }

        NotifyImageCountChanged();
        NotifySelectionChanged();
        ImagesCleared?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteSelectedImages()
    {
        List<ImageItem> selectedItems = SelectedItems.ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        string fileList = string.Join(Environment.NewLine, selectedItems.Select(image => image.FileName));
        MessageBoxResult result = MessageBox.Show(
            $"以下のファイルを削除します。{Environment.NewLine}{Environment.NewLine}{fileList}",
            "削除の確認",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        List<ImageItem> deletedItems = new();
        List<string> failedFiles = new();
        ImagesDeleting?.Invoke(this, EventArgs.Empty);

        foreach (ImageItem item in selectedItems)
        {
            try
            {
                FileSystem.DeleteFile(item.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                deletedItems.Add(item);
            }
            catch
            {
                failedFiles.Add(item.FileName);
            }
        }

        foreach (ImageItem item in deletedItems)
        {
            if (!string.IsNullOrEmpty(item.Comment)) item.CommentDocument?.Update(item.FileName, string.Empty);
            Images.Remove(item);
        }

        if (deletedItems.Count > 0)
        {
            RefreshIndexes();
            NotifyImageCountChanged();
            NotifySelectionChanged();
        }

        ImagesDeleted?.Invoke(this, EventArgs.Empty);

        StatusText = failedFiles.Count == 0
            ? $"{deletedItems.Count} 件の画像をごみ箱へ移動しました。"
            : $"{deletedItems.Count} 件を削除しました。失敗: {failedFiles.Count} 件。";
        if (!FlushComments()) StatusText += " コメントの更新を保存できませんでした。";
    }

    private void RenameAllImages()
    {
        if (!FlushComments())
        {
            StatusText = "コメントを保存できません。リネームを中止しました。";
            return;
        }
        if (Images.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(RenamePrefix))
        {
            MessageBox.Show("リネーム用の名前を入力してください。", "リネーム", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (RenameStartNumber < 0)
        {
            MessageBox.Show("開始番号には0以上の整数を入力してください。", "リネーム", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        List<ImageItem> targetImages = SelectedItems.Count > 0
            ? Images.Where(image => image.IsSelected).ToList()
            : Images.ToList();

        long maxNumber = (long)RenameStartNumber + targetImages.Count - 1;
        int paddingWidth = Math.Max(3, maxNumber.ToString().Length);
        List<RenamePlan> plans = targetImages
            .Select((image, offset) =>
            {
                long index = (long)RenameStartNumber + offset;
                string directory = Path.GetDirectoryName(image.FullPath) ?? string.Empty;
                string fileName = RenameFormat.CreateFileName(RenamePrefix, image.FileName, index, paddingWidth);
                string targetPath = Path.Combine(directory, fileName);
                return new RenamePlan(image, image.FullPath, targetPath);
            })
            .ToList();

        if (plans.Select(plan => plan.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plans.Count)
        {
            MessageBox.Show("リネーム後のファイル名が重複するため中止しました。書式を変更してください。", "リネーム", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        plans = plans.Where(plan => !StringComparer.OrdinalIgnoreCase.Equals(plan.SourcePath, plan.TargetPath)).ToList();

        if (plans.Count == 0)
        {
            StatusText = "リネーム対象はありません。";
            return;
        }

        HashSet<string> sourcePaths = targetImages.Select(image => image.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        RenamePlan? conflict = plans.FirstOrDefault(plan => File.Exists(plan.TargetPath) && !sourcePaths.Contains(plan.TargetPath));
        if (conflict is not null)
        {
            MessageBox.Show(
                $"既存ファイルと衝突するため中止しました。\n{conflict.TargetPath}",
                "リネーム",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        List<(RenamePlan Plan, string TempPath)> tempPlans = new();
        try
        {
            foreach (RenamePlan plan in plans)
            {
                string directory = Path.GetDirectoryName(plan.SourcePath) ?? string.Empty;
                string tempPath = Path.Combine(directory, $".lect-renamer-{Guid.NewGuid():N}.tmp");
                File.Move(plan.SourcePath, tempPath);
                tempPlans.Add((plan, tempPath));
            }

            foreach ((RenamePlan plan, string tempPath) in tempPlans)
            {
                File.Move(tempPath, plan.TargetPath);
                plan.Image.ApplyRenamedPath(plan.TargetPath);
            }
        }
        catch (Exception ex)
        {
            RollBackRenames(tempPlans);
            MessageBox.Show($"リネームに失敗しました。\n{ex.Message}", "リネーム", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RefreshIndexes();
        StatusText = $"{plans.Count} 件を連番リネームしました。";
        foreach (var group in plans.GroupBy(plan => plan.Image.CommentDocument))
        {
            group.Key?.Rename(group.Select(plan => (Path.GetFileName(plan.SourcePath), Path.GetFileName(plan.TargetPath))).ToList());
        }
        if (!FlushComments()) StatusText += " コメントのファイル名更新を保存できませんでした。";
    }

    private static void RollBackRenames(IEnumerable<(RenamePlan Plan, string TempPath)> tempPlans)
    {
        foreach ((RenamePlan plan, string tempPath) in tempPlans)
        {
            try
            {
                if (File.Exists(tempPath) && !File.Exists(plan.SourcePath))
                {
                    File.Move(tempPath, plan.SourcePath);
                }
            }
            catch
            {
                // Best-effort rollback; the user-facing failure already reports the original error.
            }
        }
    }

    private IEnumerable<string> ExpandPath(string path)
    {
        if (File.Exists(path))
        {
            string extension = Path.GetExtension(path);
            if (SupportedExtensions.Contains(extension))
            {
                yield return Path.GetFullPath(path);
            }

            yield break;
        }

        if (!Directory.Exists(path))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(path, "*.*", System.IO.SearchOption.AllDirectories).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            if (SupportedExtensions.Contains(Path.GetExtension(file)))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    private void RefreshIndexes()
    {
        for (int index = 0; index < Images.Count; index++)
        {
            Images[index].DisplayIndex = index;
        }
    }

    private void NotifyImageCountChanged()
    {
        RenameCommand.RaiseCanExecuteChanged();
        DeleteSelectedCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoImages));
        OnPropertyChanged(nameof(DropHint));
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

    private sealed record RenamePlan(ImageItem Image, string SourcePath, string TargetPath);
}
